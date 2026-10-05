using CommonService.Infrastructure.Modules.Identity;

namespace CommonService.Tests.Identity;

public class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Hash_produces_valid_format_within_max_length()
    {
        var hash = _hasher.Hash("Admin@123456");

        Assert.NotNull(hash);
        Assert.StartsWith("pbkdf2$sha256$100000$", hash);
        Assert.True(hash.Length <= 255, $"Hash length {hash.Length} exceeds 255");
    }

    [Fact]
    public void Verify_returns_true_for_matching_password()
    {
        var password = "SuperSecretPassword#2026";
        var hash = _hasher.Hash(password);

        Assert.True(_hasher.Verify(password, hash));
    }

    [Fact]
    public void Verify_returns_false_for_wrong_password()
    {
        var hash = _hasher.Hash("CorrectPassword");

        Assert.False(_hasher.Verify("WrongPassword", hash));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("plain_text_not_a_hash")]
    [InlineData("pbkdf2$md5$1000$salt$hash")]
    [InlineData("pbkdf2$sha256$invalid_iter$salt$hash")]
    [InlineData("pbkdf2$sha256$1000$not_valid_base64$$$")]
    public void Verify_returns_false_for_invalid_hashes_without_throwing(string? invalidHash)
    {
        var result = _hasher.Verify("SomePassword", invalidHash!);
        Assert.False(result);
    }
}
