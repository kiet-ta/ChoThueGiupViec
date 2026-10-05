namespace CommonService.Application.Features.Identity.Dtos;

/// <summary>
/// Response data for POST /api/auth/otp/request (contract identity.md §2.1).
/// </summary>
public sealed class OtpRequestResponseDto
{
    /// <summary>Seconds until the generated OTP code expires (default 300).</summary>
    public int ExpiresInSeconds { get; set; }

    /// <summary>Cooldown seconds until another OTP request is allowed (default 60).</summary>
    public int ResendAvailableInSeconds { get; set; }
}
