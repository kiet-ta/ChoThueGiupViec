using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommonService.Application.Common.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Identity.Services;

/// <summary>
/// Service producing and validating cryptographically signed JWT tokens and refresh token hashes (BE-M1-02).
/// Adheres strictly to RFC 7519 JWT and decisions Q16, SC-7.
/// </summary>
public sealed class TokenService : ITokenService
{
    private static readonly string JwtHeaderBase64 = Base64UrlEncode(
        Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));

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
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expiresAt = nowUnix + expiresInSeconds;
        var jti = Guid.NewGuid().ToString("N");

        var payload = new
        {
            sub = userId.ToString(),
            role,
            jti,
            iat = nowUnix,
            exp = expiresAt
        };

        var token = CreateSignedJwt(payload);
        return (token, expiresInSeconds);
    }

    public string GenerateRefreshToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public string HashRefreshToken(string rawRefreshToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawRefreshToken));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public (string RegistrationToken, int ExpiresInSeconds) GenerateWorkerRegistrationToken(string phoneNumber)
    {
        var expiresInSeconds = _rules.Auth.RegistrationTokenMinutes * 60;
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expiresAt = nowUnix + expiresInSeconds;
        var jti = Guid.NewGuid().ToString("N");

        var payload = new
        {
            purpose = "worker_registration",
            phone = phoneNumber,
            jti,
            iat = nowUnix,
            exp = expiresAt
        };

        var token = CreateSignedJwt(payload);
        return (token, expiresInSeconds);
    }

    public ClaimsPrincipal? ValidateAccessToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        var headerBase64 = parts[0];
        var payloadBase64 = parts[1];
        var signatureBase64 = parts[2];

        // 1. Verify HMAC-SHA256 signature
        var contentBytes = Encoding.UTF8.GetBytes($"{headerBase64}.{payloadBase64}");
        using var hmac = new HMACSHA256(_hmacKey);
        var expectedSig = Base64UrlEncode(hmac.ComputeHash(contentBytes));

        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(signatureBase64),
            Encoding.UTF8.GetBytes(expectedSig)))
        {
            return null;
        }

        // 2. Parse payload and check expiration
        try
        {
            var payloadBytes = Base64UrlDecode(payloadBase64);
            var payloadNode = JsonNode.Parse(payloadBytes);
            if (payloadNode is not JsonObject jsonObject)
            {
                return null;
            }

            var exp = jsonObject["exp"]?.GetValue<long>();
            if (exp == null || exp.Value < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                return null; // Expired
            }

            var sub = jsonObject["sub"]?.GetValue<string>();
            var role = jsonObject["role"]?.GetValue<string>();
            var jti = jsonObject["jti"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(sub) || string.IsNullOrWhiteSpace(role))
            {
                return null;
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, sub),
                new("sub", sub),
                new(ClaimTypes.Role, role),
                new("role", role)
            };

            if (!string.IsNullOrWhiteSpace(jti))
            {
                claims.Add(new Claim("jti", jti));
            }

            var identity = new ClaimsIdentity(claims, "Bearer");
            return new ClaimsPrincipal(identity);
        }
        catch
        {
            return null;
        }
    }

    private string CreateSignedJwt(object payloadObj)
    {
        var json = JsonSerializer.Serialize(payloadObj);
        var payloadBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(json));
        var contentToSign = $"{JwtHeaderBase64}.{payloadBase64}";

        using var hmac = new HMACSHA256(_hmacKey);
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(contentToSign));
        var signatureBase64 = Base64UrlEncode(signatureBytes);

        return $"{contentToSign}.{signatureBase64}";
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var output = input.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 2: output += "=="; break;
            case 3: output += "="; break;
        }
        return Convert.FromBase64String(output);
    }
}
