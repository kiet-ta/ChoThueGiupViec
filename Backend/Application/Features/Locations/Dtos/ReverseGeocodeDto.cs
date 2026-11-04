namespace CommonService.Application.Features.Locations.Dtos;

/// <summary>
/// Request to reverse geocode a WGS84 GPS coordinate into structured address components.
/// </summary>
public class ReverseGeocodeRequest
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

/// <summary>
/// Structured address resulting from reverse geocoding, mapped to TỔ ẤM administrative hierarchy.
/// </summary>
public class ReverseGeocodeResult
{
    /// <summary>Province or municipality matching vnAdminUnits (e.g. "Thành phố Hồ Chí Minh", "Thành phố Hà Nội").</summary>
    public string City { get; set; } = string.Empty;

    /// <summary>Ward or commune matching vnAdminUnits (e.g. "Phường Bến Nghé", "Xã Sóc Sơn").</summary>
    public string District { get; set; } = string.Empty;

    /// <summary>Street address including house number, route, and legacy district if applicable.</summary>
    public string AddressLine { get; set; } = string.Empty;

    /// <summary>Full human-readable address as formatted by geocoding provider.</summary>
    public string FormattedAddress { get; set; } = string.Empty;

    /// <summary>Original or snapped latitude.</summary>
    public double Latitude { get; set; }

    /// <summary>Original or snapped longitude.</summary>
    public double Longitude { get; set; }
}
