using System.Security.Claims;

namespace CommonService.Application.Features.Identity.Services;

/// <summary>
/// Generates and validates access, refresh, and registration tokens for Identity operations.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Generates a signed Bearer access token for the given user ID and role.
    /// </summary>
    (string AccessToken, int ExpiresInSeconds) GenerateAccessToken(int userId, string role);

    /// <summary>
    /// Generates a secure random raw refresh token string.
    /// </summary>
    string GenerateRefreshToken();

    /// <summary>
    /// Computes the SHA-256 hash of a raw refresh token to store in REFRESH_TOKEN.token_hash (SC-7).
    /// </summary>
    string HashRefreshToken(string rawRefreshToken);

    /// <summary>
    /// Generates a single-purpose registration token for a verified worker phone (decision O1).
    /// </summary>
    (string RegistrationToken, int ExpiresInSeconds) GenerateWorkerRegistrationToken(string phoneNumber);

    /// <summary>
    /// Validates an access token and returns the ClaimsPrincipal if valid, or null if invalid/expired.
    /// </summary>
    ClaimsPrincipal? ValidateAccessToken(string token);
}
