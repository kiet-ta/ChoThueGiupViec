namespace CommonService.Application.Features.Customers.Dtos;

/// <summary>
/// Customer profile returned by GET/PUT /api/customers/me (contract customers.md §1).
/// otp_verified_at and updated_at are intentionally not exposed.
/// </summary>
public sealed class CustomerProfileDto
{
    public int CustomerId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public decimal TrustScore { get; set; }

    /// <summary>Database spelling: ACTIVE or LOCKED (identity.md O6).</summary>
    public string AccountStatus { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
