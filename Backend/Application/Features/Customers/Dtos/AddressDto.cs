namespace CommonService.Application.Features.Customers.Dtos;

/// <summary>
/// Customer address returned by the address book endpoints (contract customers.md §1).
/// totalAreaM2 is computed by the server: floorAreaM2 x numFloors (PRD §1.1).
/// </summary>
public sealed class AddressDto
{
    public int AddressId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string AddressLine { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;

    /// <summary>APARTMENT, HOUSE or ROOM.</summary>
    public string HousingType { get; set; } = string.Empty;

    public decimal FloorAreaM2 { get; set; }
    public int NumFloors { get; set; }
    public decimal TotalAreaM2 { get; set; }
    public int? Bedrooms { get; set; }
    public int? Bathrooms { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; }
}
