namespace CommonService.Application.Features.Customers.Dtos;

/// <summary>
/// Body of POST and PUT /api/customers/me/addresses (contract customers.md §2.2).
/// There is no totalAreaM2 on purpose: the server computes it and ignores any value the client sends.
/// Numeric fields are nullable only so that a missing value is reported as a validation error instead of a silent 0.
/// </summary>
public sealed class AddressRequestDto
{
    public string Label { get; set; } = string.Empty;
    public string AddressLine { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;

    /// <summary>APARTMENT, HOUSE or ROOM.</summary>
    public string HousingType { get; set; } = string.Empty;

    public decimal? FloorAreaM2 { get; set; }
    public int? NumFloors { get; set; }
    public int? Bedrooms { get; set; }
    public int? Bathrooms { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public bool IsDefault { get; set; }
}
