namespace CommonService.Application.Features.Identity.Dtos;

/// <summary>
/// Response data for GET /api/auth/me (contract identity.md §2.6).
/// </summary>
public sealed class CurrentUserDto
{
    public int Id { get; set; }
    public string Role { get; set; } = string.Empty;
}
