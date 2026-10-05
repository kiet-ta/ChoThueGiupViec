using CommonService.Application.Common.Models;
using CommonService.Application.Features.Identity.Dtos;
using CommonService.Application.Features.Identity.Services;
using CommonService.WebAPI.Controllers.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CommonService.Tests.Identity;

public class AuthControllerTests
{
    private sealed class FakeOtpService : IOtpService
    {
        public OtpRequestResult RequestResult { get; set; } = OtpRequestResult.Ok(new OtpRequestResponseDto
        {
            ExpiresInSeconds = 300,
            ResendAvailableInSeconds = 60
        });

        public OtpVerifyResult VerifyResult { get; set; } = OtpVerifyResult.Ok(new AuthResultDto
        {
            TokenType = "Bearer",
            AccessToken = "test-token",
            AccessTokenExpiresInSeconds = 900,
            RefreshToken = "test-refresh",
            User = new AuthUserDto { Id = 1, Role = "Customer", IsNewUser = false }
        });

        public Task<OtpRequestResult> RequestOtpAsync(string phoneNumber, string roleString, string clientIp, CancellationToken ct = default)
        {
            return Task.FromResult(RequestResult);
        }

        public Task<OtpVerifyResult> VerifyOtpAsync(string phoneNumber, string roleString, string code, CancellationToken ct = default)
        {
            return Task.FromResult(VerifyResult);
        }
    }

    private static (AuthController controller, FakeOtpService service, DefaultHttpContext httpContext) CreateController()
    {
        var service = new FakeOtpService();
        var controller = new AuthController(service);
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
        return (controller, service, httpContext);
    }

    [Fact]
    public async Task RequestOtp_returns_200_ok_on_success()
    {
        var (controller, _, _) = CreateController();

        var actionResult = await controller.RequestOtp(new OtpRequestDto
        {
            PhoneNumber = "0901234567",
            Role = "Customer"
        }, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);

        var apiResponse = Assert.IsType<ApiResponse<OtpRequestResponseDto>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.NotNull(apiResponse.Data);
        Assert.Equal(300, apiResponse.Data.ExpiresInSeconds);
    }

    [Fact]
    public async Task RequestOtp_returns_429_with_RetryAfter_header_when_rate_limited()
    {
        var (controller, service, httpContext) = CreateController();
        service.RequestResult = OtpRequestResult.RateLimited(45, "Cooldown active");

        var actionResult = await controller.RequestOtp(new OtpRequestDto
        {
            PhoneNumber = "0901234567",
            Role = "Customer"
        }, CancellationToken.None);

        var statusResult = Assert.IsType<ObjectResult>(actionResult);
        Assert.Equal(StatusCodes.Status429TooManyRequests, statusResult.StatusCode);
        Assert.Equal("45", httpContext.Response.Headers["Retry-After"].ToString());

        var apiResponse = Assert.IsType<ApiResponse<object>>(statusResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("Cooldown active", apiResponse.Message);
    }

    [Fact]
    public async Task RequestOtp_returns_400_bad_request_on_validation_failure()
    {
        var (controller, service, _) = CreateController();
        service.RequestResult = OtpRequestResult.ValidationError(new Dictionary<string, string[]>
        {
            ["phoneNumber"] = ["Invalid format"]
        });

        var actionResult = await controller.RequestOtp(new OtpRequestDto
        {
            PhoneNumber = "invalid",
            Role = "Customer"
        }, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);

        var apiResponse = Assert.IsType<ApiResponse<object>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
    }

    [Fact]
    public async Task VerifyOtp_returns_200_with_AuthResult_for_valid_code()
    {
        var (controller, _, _) = CreateController();

        var actionResult = await controller.VerifyOtp(new OtpVerifyDto
        {
            PhoneNumber = "0901234567",
            Role = "Customer",
            Code = "123456"
        }, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);

        var apiResponse = Assert.IsType<ApiResponse<AuthResultDto>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.NotNull(apiResponse.Data);
        Assert.Equal("Customer", apiResponse.Data.User.Role);
    }

    [Fact]
    public async Task VerifyOtp_returns_200_with_RegistrationRequired_for_new_worker()
    {
        var (controller, service, _) = CreateController();
        service.VerifyResult = OtpVerifyResult.RegistrationNeeded(new RegistrationRequiredDto
        {
            IsNewUser = true,
            RegistrationToken = "sample-worker-token",
            RegistrationTokenExpiresInSeconds = 1800
        });

        var actionResult = await controller.VerifyOtp(new OtpVerifyDto
        {
            PhoneNumber = "0901234567",
            Role = "Worker",
            Code = "123456"
        }, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var apiResponse = Assert.IsType<ApiResponse<RegistrationRequiredDto>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.NotNull(apiResponse.Data);
        Assert.True(apiResponse.Data.IsNewUser);
        Assert.Equal("sample-worker-token", apiResponse.Data.RegistrationToken);
    }

    [Fact]
    public async Task VerifyOtp_returns_401_unauthorized_on_invalid_or_expired_code()
    {
        var (controller, service, _) = CreateController();
        service.VerifyResult = OtpVerifyResult.Unauthorized("Invalid or expired OTP code.");

        var actionResult = await controller.VerifyOtp(new OtpVerifyDto
        {
            PhoneNumber = "0901234567",
            Role = "Customer",
            Code = "999999"
        }, CancellationToken.None);

        var statusResult = Assert.IsType<ObjectResult>(actionResult);
        Assert.Equal(StatusCodes.Status401Unauthorized, statusResult.StatusCode);

        var apiResponse = Assert.IsType<ApiResponse<object>>(statusResult.Value);
        Assert.False(apiResponse.Success);
    }

    [Fact]
    public async Task VerifyOtp_returns_403_forbidden_when_account_locked()
    {
        var (controller, service, _) = CreateController();
        service.VerifyResult = OtpVerifyResult.Forbidden("Account is locked.");

        var actionResult = await controller.VerifyOtp(new OtpVerifyDto
        {
            PhoneNumber = "0901234567",
            Role = "Customer",
            Code = "123456"
        }, CancellationToken.None);

        var statusResult = Assert.IsType<ObjectResult>(actionResult);
        Assert.Equal(StatusCodes.Status403Forbidden, statusResult.StatusCode);

        var apiResponse = Assert.IsType<ApiResponse<object>>(statusResult.Value);
        Assert.False(apiResponse.Success);
    }

    [Fact]
    public async Task VerifyOtp_returns_429_with_RetryAfter_header_when_attempts_exceeded()
    {
        var (controller, service, httpContext) = CreateController();
        service.VerifyResult = OtpVerifyResult.TooManyAttempts(60);

        var actionResult = await controller.VerifyOtp(new OtpVerifyDto
        {
            PhoneNumber = "0901234567",
            Role = "Customer",
            Code = "000000"
        }, CancellationToken.None);

        var statusResult = Assert.IsType<ObjectResult>(actionResult);
        Assert.Equal(StatusCodes.Status429TooManyRequests, statusResult.StatusCode);
        Assert.Equal("60", httpContext.Response.Headers["Retry-After"].ToString());

        var apiResponse = Assert.IsType<ApiResponse<object>>(statusResult.Value);
        Assert.False(apiResponse.Success);
    }
}
