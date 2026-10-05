namespace CommonService.Application.Features.Customers.Dtos;

/// <summary>
/// Request payload for PUT /api/customers/me: full replace of the editable fields (contract customers.md §2.1).
/// phoneNumber, trustScore and accountStatus are not editable.
/// </summary>
public sealed class UpdateCustomerProfileDto
{
    /// <summary>Required, 1-100 characters after trimming.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Optional, valid address up to 255 characters. Null or blank clears the email.</summary>
    public string? Email { get; set; }
}
