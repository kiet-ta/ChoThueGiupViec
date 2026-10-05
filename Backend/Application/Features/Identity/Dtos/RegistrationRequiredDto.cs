namespace CommonService.Application.Features.Identity.Dtos;

/// <summary>
/// Response data when a new Worker verifies phone but must complete registration (contract identity.md §2.2, decision O1).
/// </summary>
public sealed class RegistrationRequiredDto
{
    public bool IsNewUser { get; set; } = true;
    public string RegistrationToken { get; set; } = string.Empty;
    public int RegistrationTokenExpiresInSeconds { get; set; }
}
