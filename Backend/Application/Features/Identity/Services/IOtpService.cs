namespace CommonService.Application.Features.Identity.Services;

/// <summary>
/// Service managing OTP issuance, rate limiting, and verification for Customer/Worker login.
/// </summary>
public interface IOtpService
{
    /// <summary>
    /// Issues a 6-digit OTP code if rate limits and cooldown permit.
    /// </summary>
    Task<OtpRequestResult> RequestOtpAsync(string phoneNumber, string roleString, string clientIp, CancellationToken ct = default);

    /// <summary>
    /// Verifies the OTP code, tracks attempts, creates Customer on first login, and returns authentication tokens.
    /// </summary>
    Task<OtpVerifyResult> VerifyOtpAsync(string phoneNumber, string roleString, string code, CancellationToken ct = default);
}
