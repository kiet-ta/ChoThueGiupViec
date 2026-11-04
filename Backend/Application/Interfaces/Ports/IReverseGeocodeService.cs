using CommonService.Application.Features.Locations.Dtos;

namespace CommonService.Application.Interfaces.Ports;

/// <summary>
/// Port for reverse geocoding GPS coordinates (lat, lng) into Vietnamese structured addresses.
/// Implemented by GoongReverseGeocodeService (real) and FakeReverseGeocodeService (in-memory/CI).
/// </summary>
public interface IReverseGeocodeService
{
    Task<ReverseGeocodeResult?> ReverseGeocodeAsync(double latitude, double longitude, CancellationToken ct = default);
}
