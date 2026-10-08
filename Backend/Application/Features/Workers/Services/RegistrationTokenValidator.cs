using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace CommonService.Application.Features.Workers.Services;

public static class RegistrationTokenValidator
{
    public static string? ValidateRegistrationToken(string token, IConfiguration configuration)
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

        var secret = configuration["Jwt:Key"]
            ?? configuration["Otp:HmacSecret"]
            ?? "ChoThueGiupViec_Dev_Default_Hmac_Secret_Key_At_Least_32_Chars!";
        var hmacKey = Encoding.UTF8.GetBytes(secret);

        var headerBase64 = parts[0];
        var payloadBase64 = parts[1];
        var signatureBase64 = parts[2];

        // 1. Verify HMAC signature
        var contentBytes = Encoding.UTF8.GetBytes($"{headerBase64}.{payloadBase64}");
        using var hmac = new HMACSHA256(hmacKey);
        var expectedSig = Base64UrlEncode(hmac.ComputeHash(contentBytes));

        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(signatureBase64),
            Encoding.UTF8.GetBytes(expectedSig)))
        {
            return null;
        }

        // 2. Parse payload
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
                return null; // Expired token
            }

            var purpose = jsonObject["purpose"]?.GetValue<string>();
            if (purpose != "worker_registration")
            {
                return null; // Invalid token purpose
            }

            var phone = jsonObject["phone"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(phone) ? null : phone;
        }
        catch
        {
            return null;
        }
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
