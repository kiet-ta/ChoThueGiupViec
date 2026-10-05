namespace CommonService.Application.Features.Identity.Dtos;

/// <summary>
/// Successful authentication response data (contract identity.md §2.2).
/// </summary>
public sealed class AuthResultDto
{
    public string TokenType { get; set; } = "Bearer";
    public string AccessToken { get; set; } = string.Empty;
    public int AccessTokenExpiresInSeconds { get; set; }
    public string RefreshToken { get; set; } = string.Empty;
    public AuthUserDto User { get; set; } = new();
}
