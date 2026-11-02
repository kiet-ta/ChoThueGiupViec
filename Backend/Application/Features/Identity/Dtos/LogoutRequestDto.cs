namespace CommonService.Application.Features.Identity.Dtos;

/// <summary>
/// Request payload for POST /api/auth/logout (contract identity.md §2.5).
/// </summary>
public class LogoutRequestDto
{
    public string RefreshToken { get; set; } = string.Empty;
}
