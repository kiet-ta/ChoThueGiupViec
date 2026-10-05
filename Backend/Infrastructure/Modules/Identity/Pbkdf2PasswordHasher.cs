using System.Security.Cryptography;
using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Modules.Identity;

/// <summary>
/// Production password hasher using PBKDF2 with HMAC-SHA256 (decisions Q16).
/// Format: <c>pbkdf2$sha256${iterations}${saltBase64}${hashBase64}</c>.
/// Fits well within <c>VARCHAR(255)</c> (~88 chars).
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltBytes = 16;
    private const int SubkeyBytes = 32;
    private const int DefaultIterations = 100_000;
    private const string Identifier = "pbkdf2";
    private const string Algorithm = "sha256";

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var subkey = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            DefaultIterations,
            HashAlgorithmName.SHA256,
            SubkeyBytes);

        return $"{Identifier}${Algorithm}${DefaultIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(subkey)}";
    }

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hash))
        {
            return false;
        }

        var parts = hash.Split('$');
        if (parts.Length != 5)
        {
            return false;
        }

        if (!string.Equals(parts[0], Identifier, StringComparison.Ordinal) ||
            !string.Equals(parts[1], Algorithm, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!int.TryParse(parts[2], out var iterations) || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expectedSubkey;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            expectedSubkey = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actualSubkey = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expectedSubkey.Length);

        return CryptographicOperations.FixedTimeEquals(actualSubkey, expectedSubkey);
    }
}
