using CommonService.Application.Features.Identity.Dtos;

namespace CommonService.Application.Features.Identity.Services;

public sealed class OtpRequestResult
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public IDictionary<string, string[]>? ValidationErrors { get; init; }
    public int? RetryAfterSeconds { get; init; }
    public OtpRequestResponseDto? Data { get; init; }

    public static OtpRequestResult Ok(OtpRequestResponseDto data) => new()
    {
        Success = true,
        StatusCode = 200,
        Data = data
    };

    public static OtpRequestResult ValidationError(IDictionary<string, string[]> errors) => new()
    {
        Success = false,
        StatusCode = 400,
        ErrorMessage = "Validation failed",
        ValidationErrors = errors
    };

    public static OtpRequestResult RateLimited(int retryAfterSeconds, string message = "Too many requests. Please try again later.") => new()
    {
        Success = false,
        StatusCode = 429,
        ErrorMessage = message,
        RetryAfterSeconds = retryAfterSeconds
    };
}

public sealed class OtpVerifyResult
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public IDictionary<string, string[]>? ValidationErrors { get; init; }
    public int? RetryAfterSeconds { get; init; }
    public AuthResultDto? AuthResult { get; init; }
    public RegistrationRequiredDto? RegistrationRequired { get; init; }

    public static OtpVerifyResult Ok(AuthResultDto authResult) => new()
    {
        Success = true,
        StatusCode = 200,
        AuthResult = authResult
    };

    public static OtpVerifyResult RegistrationNeeded(RegistrationRequiredDto regResult) => new()
    {
        Success = true,
        StatusCode = 200,
        RegistrationRequired = regResult
    };

    public static OtpVerifyResult ValidationError(IDictionary<string, string[]> errors) => new()
    {
        Success = false,
        StatusCode = 400,
        ErrorMessage = "Validation failed",
        ValidationErrors = errors
    };

    public static OtpVerifyResult Unauthorized(string message = "Invalid or expired OTP code.") => new()
    {
        Success = false,
        StatusCode = 401,
        ErrorMessage = message
    };

    public static OtpVerifyResult Forbidden(string message = "Account is locked or disabled.") => new()
    {
        Success = false,
        StatusCode = 403,
        ErrorMessage = message
    };

    public static OtpVerifyResult TooManyAttempts(int retryAfterSeconds = 60, string message = "Maximum verification attempts exceeded. Please request a new OTP code.") => new()
    {
        Success = false,
        StatusCode = 429,
        ErrorMessage = message,
        RetryAfterSeconds = retryAfterSeconds
    };
}

public sealed class RefreshResult
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public AuthResultDto? Data { get; init; }

    public static RefreshResult Ok(AuthResultDto data) => new()
    {
        Success = true,
        StatusCode = 200,
        Data = data
    };

    public static RefreshResult BadRequest(string message = "Missing or invalid refresh token.") => new()
    {
        Success = false,
        StatusCode = 400,
        ErrorMessage = message
    };

    public static RefreshResult Unauthorized(string message = "Invalid, expired, or revoked refresh token.") => new()
    {
        Success = false,
        StatusCode = 401,
        ErrorMessage = message
    };
}

