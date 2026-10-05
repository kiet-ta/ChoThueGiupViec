using CommonService.Application.Features.Identity.Dtos;

namespace CommonService.Application.Features.Identity.Services;

public sealed class PasswordLoginResult
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public IDictionary<string, string[]>? ValidationErrors { get; init; }
    public int? RetryAfterSeconds { get; init; }
    public AuthResultDto? Data { get; init; }

    public static PasswordLoginResult Ok(AuthResultDto data) => new()
    {
        Success = true,
        StatusCode = 200,
        Data = data
    };

    public static PasswordLoginResult ValidationError(IDictionary<string, string[]> errors) => new()
    {
        Success = false,
        StatusCode = 400,
        ErrorMessage = "Validation failed",
        ValidationErrors = errors
    };

    /// <summary>Unknown email and wrong password share one response (contract §2.3).</summary>
    public static PasswordLoginResult Unauthorized(string message = "Invalid email or password.") => new()
    {
        Success = false,
        StatusCode = 401,
        ErrorMessage = message
    };

    public static PasswordLoginResult Forbidden(string message = "Account is disabled.") => new()
    {
        Success = false,
        StatusCode = 403,
        ErrorMessage = message
    };

    public static PasswordLoginResult Locked(int retryAfterSeconds, string message = "Account is temporarily locked after repeated failed logins.") => new()
    {
        Success = false,
        StatusCode = 423,
        ErrorMessage = message,
        RetryAfterSeconds = retryAfterSeconds
    };
}
