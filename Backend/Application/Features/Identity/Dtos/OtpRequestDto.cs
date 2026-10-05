namespace CommonService.Application.Features.Identity.Dtos;

/// <summary>
/// Request payload for POST /api/auth/otp/request (contract identity.md §2.1).
/// </summary>
public sealed class OtpRequestDto
{
    /// <summary>Vietnamese mobile number (0XXXXXXXXX).</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Role: "Customer" or "Worker".</summary>
    public string Role { get; set; } = string.Empty;
}
