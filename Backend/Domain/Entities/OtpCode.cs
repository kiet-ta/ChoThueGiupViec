using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table OTP_CODE. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class OtpCode
{
    /// <summary>otp_code.otp_id BIGINT (PK)</summary>
    public long OtpId { get; set; }

    /// <summary>otp_code.phone_number VARCHAR(15)</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>otp_code.role VARCHAR(10)</summary>
    public UserRole Role { get; set; }

    /// <summary>otp_code.code_hash VARCHAR(255)</summary>
    public string CodeHash { get; set; } = string.Empty;

    /// <summary>otp_code.attempt_count TINYINT</summary>
    public byte AttemptCount { get; set; }

    /// <summary>otp_code.requested_ip VARCHAR(45)</summary>
    public string RequestedIp { get; set; } = string.Empty;

    /// <summary>otp_code.created_at DATETIME2</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>otp_code.expires_at DATETIME2</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>otp_code.consumed_at DATETIME2 NULL</summary>
    public DateTime? ConsumedAt { get; set; }
}
