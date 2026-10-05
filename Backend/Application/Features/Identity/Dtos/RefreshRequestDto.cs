namespace CommonService.Application.Features.Identity.Dtos;

/// <summary>
/// Request payload for POST /api/auth/refresh (contract identity.md §2.4).
/// </summary>
public sealed class RefreshRequestDto
{
    public string RefreshToken { get; set; } = string.Empty;
}
