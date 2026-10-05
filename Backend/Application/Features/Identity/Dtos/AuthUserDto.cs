namespace CommonService.Application.Features.Identity.Dtos;

/// <summary>
/// Authenticated user summary in AuthResultDto (contract identity.md §2.2).
/// </summary>
public sealed class AuthUserDto
{
    public int Id { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsNewUser { get; set; }
}
