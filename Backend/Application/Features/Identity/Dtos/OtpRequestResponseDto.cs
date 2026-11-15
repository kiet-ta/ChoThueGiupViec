using System.Text.Json.Serialization;

namespace CommonService.Application.Features.Identity.Dtos;

/// <summary>
/// Response data for POST /api/v1/auth/otp/request (contract identity.md §2.1).
/// </summary>
public class OtpRequestResponseDto
{
    /// <summary>Seconds until the generated OTP code expires (default 300).</summary>
    public int ExpiresInSeconds { get; set; }

    /// <summary>Cooldown seconds until another OTP request is allowed (default 60).</summary>
    public int ResendAvailableInSeconds { get; set; }

    /// <summary>Plain OTP code returned only in Development when FakeOtpSender is active; omitted otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SandboxOtp { get; set; }
}
