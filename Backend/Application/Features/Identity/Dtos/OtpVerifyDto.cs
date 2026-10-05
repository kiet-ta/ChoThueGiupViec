namespace CommonService.Application.Features.Identity.Dtos;

/// <summary>
/// Request payload for POST /api/auth/otp/verify (contract identity.md §2.2).
/// </summary>
public sealed class OtpVerifyDto
{
    /// <summary>Vietnamese mobile number (0XXXXXXXXX).</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Role: "Customer" or "Worker".</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>6-digit verification code.</summary>
    public string Code { get; set; } = string.Empty;
}
