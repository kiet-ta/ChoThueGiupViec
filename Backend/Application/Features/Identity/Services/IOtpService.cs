namespace CommonService.Application.Features.Identity.Services;

/// <summary>
/// Service managing OTP issuance, rate limiting, and verification for Customer/Worker login,
/// as well as refresh token rotation and logout (contract identity.md §2.1, §2.2, §2.4, §2.5).
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

    /// <summary>
    /// Rotates a refresh token: issues new access and refresh tokens, revokes previous token,
    /// and detects token reuse (revoking entire token family) per contract §2.4 and SC-7.
    /// </summary>
    Task<RefreshResult> RefreshTokenAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>
    /// Revokes a refresh token on logout (idempotent, contract §2.5).
    /// </summary>
    Task LogoutAsync(string refreshToken, CancellationToken ct = default);
}
