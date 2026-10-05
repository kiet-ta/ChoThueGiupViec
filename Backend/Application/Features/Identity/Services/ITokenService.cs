namespace CommonService.Application.Features.Identity.Services;

/// <summary>
/// Generates access, refresh, and registration tokens for Identity operations.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Generates a signed Bearer access token for the given user ID and role.
    /// </summary>
    (string AccessToken, int ExpiresInSeconds) GenerateAccessToken(int userId, string role);

    /// <summary>
    /// Generates a secure random refresh token string.
    /// </summary>
    string GenerateRefreshToken();

    /// <summary>
    /// Generates a single-purpose registration token for a verified worker phone (decision O1).
    /// </summary>
    (string RegistrationToken, int ExpiresInSeconds) GenerateWorkerRegistrationToken(string phoneNumber);
}
