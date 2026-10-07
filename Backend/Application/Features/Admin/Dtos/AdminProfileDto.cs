namespace CommonService.Application.Features.Admin.Dtos;

/// <summary>
/// The signed-in Admin as shown to themselves (contract admin.md section 1, AdminProfile). The password hash and the
/// login-lockout columns of ADMIN are deliberately not part of it.
/// </summary>
public sealed class AdminProfileDto
{
    public int AdminId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string AdminRole { get; init; } = string.Empty;
    public bool IsActive { get; init; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; init; }
}
