using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CommonService.Application.Common.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Identity.Services;

/// <summary>
/// Service producing cryptographically signed tokens for Identity flows.
/// Fully compatible with BE-M1-01 and ready to be enriched in BE-M1-02.
/// </summary>
public sealed class TokenService : ITokenService
{
    private readonly BusinessRules _rules;
    private readonly byte[] _hmacKey;

    public TokenService(IOptions<BusinessRules> rules, IConfiguration configuration)
    {
        _rules = rules.Value;
        var secret = configuration["Jwt:Key"]
            ?? configuration["Otp:HmacSecret"]
            ?? "ChoThueGiupViec_Dev_Default_Hmac_Secret_Key_At_Least_32_Chars!";
        _hmacKey = Encoding.UTF8.GetBytes(secret);
    }

    public (string AccessToken, int ExpiresInSeconds) GenerateAccessToken(int userId, string role)
    {
        var expiresInSeconds = _rules.Auth.AccessTokenMinutes * 60;
        var expiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expiresInSeconds;
        var jti = Guid.NewGuid().ToString("N");

        var payload = new
        {
            sub = userId.ToString(),
            role,
            jti,
            exp = expiresAt
        };

        var token = CreateSignedToken(payload);
        return (token, expiresInSeconds);
    }

    public string GenerateRefreshToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public (string RegistrationToken, int ExpiresInSeconds) GenerateWorkerRegistrationToken(string phoneNumber)
    {
        var expiresInSeconds = _rules.Auth.RegistrationTokenMinutes * 60;
        var expiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expiresInSeconds;
        var jti = Guid.NewGuid().ToString("N");

        var payload = new
        {
            purpose = "worker_registration",
            phone = phoneNumber,
            jti,
            exp = expiresAt
        };

        var token = CreateSignedToken(payload);
        return (token, expiresInSeconds);
    }

    private string CreateSignedToken(object payloadObj)
    {
        var json = JsonSerializer.Serialize(payloadObj);
        var payloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        using var hmac = new HMACSHA256(_hmacKey);
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadBase64));
        var signatureBase64 = Convert.ToBase64String(signatureBytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{payloadBase64}.{signatureBase64}";
    }
}
