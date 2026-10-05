namespace CommonService.Application.Interfaces.Ports;

/// <summary>One-way password hashing for Admin and Partner Agency email+password login (decisions Q16).</summary>
public interface IPasswordHasher
{
    /// <summary>Hash a password into a self-describing string that fits <c>VARCHAR(255)</c>.</summary>
    string Hash(string password);

    /// <summary>True when <paramref name="password"/> matches <paramref name="hash"/>. Never throws on a malformed hash.</summary>
    bool Verify(string password, string hash);
}
