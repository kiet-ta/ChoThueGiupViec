using System.Security.Cryptography;
using System.Text;
using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Fakes;

/// <summary>Unsalted SHA-256 so tests are fast and deterministic. NOT for production: BASE-10 registers the real hasher.</summary>
public sealed class FakePasswordHasher : IPasswordHasher
{
    private const string Prefix = "fake$";

    public string Hash(string password) =>
        Prefix + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password)));

    public bool Verify(string password, string hash) =>
        hash is not null && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Hash(password)),
            Encoding.UTF8.GetBytes(hash));
}
