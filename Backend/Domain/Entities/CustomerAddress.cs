using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table CUSTOMER_ADDRESS. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class CustomerAddress
{
    /// <summary>customer_address.address_id INT (PK)</summary>
    public int AddressId { get; set; }

    /// <summary>customer_address.customer_id INT (FK)</summary>
    public int CustomerId { get; set; }

    /// <summary>customer_address.label NVARCHAR(50)</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>customer_address.address_line NVARCHAR(255)</summary>
    public string AddressLine { get; set; } = string.Empty;

    /// <summary>customer_address.district NVARCHAR(100)</summary>
    public string District { get; set; } = string.Empty;

    /// <summary>customer_address.city NVARCHAR(100)</summary>
    public string City { get; set; } = string.Empty;

    /// <summary>customer_address.housing_type VARCHAR(12)</summary>
    public HousingType HousingType { get; set; }

    /// <summary>customer_address.floor_area_m2 DECIMAL(6,2)</summary>
    public decimal FloorAreaM2 { get; set; }

    /// <summary>customer_address.num_floors TINYINT</summary>
    public byte NumFloors { get; set; }

    /// <summary>customer_address.bedrooms TINYINT NULL</summary>
    public byte? Bedrooms { get; set; }

    /// <summary>customer_address.bathrooms TINYINT NULL</summary>
    public byte? Bathrooms { get; set; }

    /// <summary>customer_address.latitude DECIMAL(9,6)</summary>
    public decimal Latitude { get; set; }

    /// <summary>customer_address.longitude DECIMAL(9,6)</summary>
    public decimal Longitude { get; set; }

    /// <summary>customer_address.is_default BIT</summary>
    public bool IsDefault { get; set; }

    /// <summary>customer_address.created_at DATETIME2</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>S_total = S_floor x N_floors (PRD 1.1). Computed column in the database (total_area_m2); computed here for the domain.</summary>
    public decimal TotalAreaM2 => FloorAreaM2 * NumFloors;
}
