using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table CUSTOMER. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class Customer
{
    /// <summary>customer.customer_id INT (PK)</summary>
    public int CustomerId { get; set; }

    /// <summary>customer.phone_number VARCHAR(15) (UNIQUE)</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>customer.full_name NVARCHAR(100)</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>customer.email VARCHAR(255) NULL</summary>
    public string? Email { get; set; }

    /// <summary>customer.otp_verified_at DATETIME2 NULL</summary>
    public DateTime? OtpVerifiedAt { get; set; }

    /// <summary>customer.trust_score DECIMAL(3,2)</summary>
    public decimal TrustScore { get; set; }

    /// <summary>customer.account_status VARCHAR(10)</summary>
    public CustomerAccountStatus AccountStatus { get; set; }

    /// <summary>customer.created_at DATETIME2</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>customer.updated_at DATETIME2</summary>
    public DateTime UpdatedAt { get; set; }
}
