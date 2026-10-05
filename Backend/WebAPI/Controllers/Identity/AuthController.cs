using CommonService.Application.Common.Models;
using CommonService.Application.Features.Identity.Dtos;
using CommonService.Application.Features.Identity.Services;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers.Identity;

/// <summary>
/// Authentication controller handling OTP login, token refresh, logout, and current user info (contract identity.md §2).
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IOtpService _otpService;
    private readonly ICurrentUser _currentUser;

    public AuthController(IOtpService otpService, ICurrentUser currentUser)
    {
        _otpService = otpService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Requests a 6-digit OTP code sent to the phone for Customer or Worker login.
    /// </summary>
    [HttpPost("otp/request")]
    [ProducesResponseType(typeof(ApiResponse<OtpRequestResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RequestOtp([FromBody] OtpRequestDto request, CancellationToken ct)
    {
        if (request == null)
        {
            return BadRequest(ApiResponse<object>.Fail("Request body is required.", null));
        }

        var clientIp = GetClientIpAddress();
        var result = await _otpService.RequestOtpAsync(request.PhoneNumber, request.Role, clientIp, ct);

        if (result.Success && result.Data != null)
        {
            return Ok(ApiResponse<OtpRequestResponseDto>.Ok(result.Data, "OTP sent successfully."));
        }

        if (result.StatusCode == StatusCodes.Status429TooManyRequests)
        {
            if (result.RetryAfterSeconds.HasValue)
            {
                Response.Headers["Retry-After"] = result.RetryAfterSeconds.Value.ToString();
            }

            return StatusCode(StatusCodes.Status429TooManyRequests,
                ApiResponse<object>.Fail(result.ErrorMessage ?? "Rate limit exceeded. Please try again later.", null));
        }

        return BadRequest(ApiResponse<object>.Fail(
            result.ErrorMessage ?? "Validation failed",
            result.ValidationErrors != null ? new { errors = result.ValidationErrors } : null));
    }

    /// <summary>
    /// Verifies the 6-digit OTP code and logs in Customer or Worker.
    /// </summary>
    [HttpPost("otp/verify")]
    [ProducesResponseType(typeof(ApiResponse<AuthResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> VerifyOtp([FromBody] OtpVerifyDto request, CancellationToken ct)
    {
        if (request == null)
        {
            return BadRequest(ApiResponse<object>.Fail("Request body is required.", null));
        }

        var result = await _otpService.VerifyOtpAsync(request.PhoneNumber, request.Role, request.Code, ct);

        if (result.Success)
        {
            if (result.AuthResult != null)
            {
                return Ok(ApiResponse<AuthResultDto>.Ok(result.AuthResult, "Authentication successful."));
            }

            if (result.RegistrationRequired != null)
            {
                return Ok(ApiResponse<RegistrationRequiredDto>.Ok(result.RegistrationRequired, "Profile registration required."));
            }
        }

        return result.StatusCode switch
        {
            StatusCodes.Status400BadRequest => BadRequest(ApiResponse<object>.Fail(
                result.ErrorMessage ?? "Validation failed",
                result.ValidationErrors != null ? new { errors = result.ValidationErrors } : null)),

            StatusCodes.Status401Unauthorized => StatusCode(StatusCodes.Status401Unauthorized,
                ApiResponse<object>.Fail(result.ErrorMessage ?? "Invalid or expired OTP code.", null)),

            StatusCodes.Status403Forbidden => StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<object>.Fail(result.ErrorMessage ?? "Account is locked.", null)),

            StatusCodes.Status429TooManyRequests => SetRetryAfterAndReturn(
                result.RetryAfterSeconds ?? 60,
                result.ErrorMessage ?? "Maximum verification attempts exceeded. Please request a new OTP code."),

            _ => StatusCode(result.StatusCode, ApiResponse<object>.Fail(result.ErrorMessage ?? "Request failed.", null))
        };
    }

    /// <summary>
    /// Rotates an existing refresh token: returns a new access token and rotated refresh token (contract §2.4).
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(ApiResponse<AuthResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshRequestDto request, CancellationToken ct)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return BadRequest(ApiResponse<object>.Fail("Refresh token is required.", null));
        }

        var result = await _otpService.RefreshTokenAsync(request.RefreshToken, ct);

        if (result.Success && result.Data != null)
        {
            return Ok(ApiResponse<AuthResultDto>.Ok(result.Data, "Token refreshed successfully."));
        }

        if (result.StatusCode == StatusCodes.Status400BadRequest)
        {
            return BadRequest(ApiResponse<object>.Fail(result.ErrorMessage ?? "Validation failed.", null));
        }

        return StatusCode(StatusCodes.Status401Unauthorized,
            ApiResponse<object>.Fail(result.ErrorMessage ?? "Invalid, expired, or revoked refresh token.", null));
    }

    /// <summary>
    /// Revokes the specified refresh token upon user logout (contract §2.5).
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequestDto request, CancellationToken ct)
    {
        if (request != null && !string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            await _otpService.LogoutAsync(request.RefreshToken, ct);
        }

        return Ok(ApiResponse<object?>.Ok(null, "Logged out successfully."));
    }

    /// <summary>
    /// Returns current authenticated user information from token claims (contract §2.6).
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<CurrentUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public IActionResult GetCurrentUser()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null || _currentUser.Role == null)
        {
            return Unauthorized(ApiResponse<object>.Fail("Unauthorized.", null));
        }

        return Ok(ApiResponse<CurrentUserDto>.Ok(new CurrentUserDto
        {
            Id = _currentUser.UserId.Value,
            Role = _currentUser.Role.Value.ToString()
        }, "Current user retrieved."));
    }

    private IActionResult SetRetryAfterAndReturn(int retryAfterSeconds, string message)
    {
        Response.Headers["Retry-After"] = retryAfterSeconds.ToString();
        return StatusCode(StatusCodes.Status429TooManyRequests, ApiResponse<object>.Fail(message, null));
    }

    private string GetClientIpAddress()
    {
        if (Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor) && !string.IsNullOrWhiteSpace(forwardedFor))
        {
            var ip = forwardedFor.ToString().Split(',')[0].Trim();
            if (!string.IsNullOrWhiteSpace(ip))
            {
                return ip;
            }
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
    }
}
