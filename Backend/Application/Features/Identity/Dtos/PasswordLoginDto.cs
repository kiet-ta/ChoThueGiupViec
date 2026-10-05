namespace CommonService.Application.Features.Identity.Dtos;

/// <summary>
/// Request payload for POST /api/auth/password/login (contract identity.md §2.3).
/// </summary>
public sealed class PasswordLoginDto
{
    /// <summary>ADMIN.email or PARTNER_AGENCY.contact_email.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Plain password; verified through IPasswordHasher, never logged.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Role: "Admin" or "Partner".</summary>
    public string Role { get; set; } = string.Empty;
}
