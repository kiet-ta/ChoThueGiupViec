using CommonService.Application.Features.Locations.Dtos;
using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Fakes;

/// <summary>
/// In-memory Fake reverse geocoding service for unit tests and local development without a Goong API key.
/// Produces deterministic structured addresses for coordinates in Vietnam and null for coordinates outside.
/// </summary>
public class FakeReverseGeocodeService : IReverseGeocodeService
{
    private Func<double, double, ReverseGeocodeResult?>? _customHandler;

    public void Override(Func<double, double, ReverseGeocodeResult?> handler)
    {
        _customHandler = handler;
    }

    public void Reset()
    {
        _customHandler = null;
    }

    public Task<ReverseGeocodeResult?> ReverseGeocodeAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        if (_customHandler != null)
        {
            return Task.FromResult(_customHandler(latitude, longitude));
        }

        // Outside Vietnam territorial bounding box
        if (latitude < 8.0 || latitude > 24.0 || longitude < 102.0 || longitude > 110.0)
        {
            return Task.FromResult<ReverseGeocodeResult?>(null);
        }

        // Ho Chi Minh City vicinity
        if (latitude >= 10.6 && latitude <= 11.0 && longitude >= 106.5 && longitude <= 107.0)
        {
            return Task.FromResult<ReverseGeocodeResult?>(new ReverseGeocodeResult
            {
                City = "Thành phố Hồ Chí Minh",
                District = "Phường Bến Nghé",
                AddressLine = "123 Lê Lợi, Quận 1",
                FormattedAddress = "123 Lê Lợi, Phường Bến Nghé, Quận 1, Thành phố Hồ Chí Minh",
                Latitude = latitude,
                Longitude = longitude,
            });
        }

        // Ha Noi vicinity
        if (latitude >= 20.8 && latitude <= 21.3 && longitude >= 105.6 && longitude <= 106.1)
        {
            return Task.FromResult<ReverseGeocodeResult?>(new ReverseGeocodeResult
            {
                City = "Thành phố Hà Nội",
                District = "Phường Hoàn Kiếm",
                AddressLine = "45 Tràng Tiền, Quận Hoàn Kiếm",
                FormattedAddress = "45 Tràng Tiền, Phường Hoàn Kiếm, Quận Hoàn Kiếm, Thành phố Hà Nội",
                Latitude = latitude,
                Longitude = longitude,
            });
        }

        // Da Nang vicinity
        if (latitude >= 15.9 && latitude <= 16.3 && longitude >= 108.0 && longitude <= 108.5)
        {
            return Task.FromResult<ReverseGeocodeResult?>(new ReverseGeocodeResult
            {
                City = "Thành phố Đà Nẵng",
                District = "Phường Hải Châu",
                AddressLine = "88 Bạch Đằng, Quận Hải Châu",
                FormattedAddress = "88 Bạch Đằng, Phường Hải Châu, Quận Hải Châu, Thành phố Đà Nẵng",
                Latitude = latitude,
                Longitude = longitude,
            });
        }

        // Generic Vietnam point
        return Task.FromResult<ReverseGeocodeResult?>(new ReverseGeocodeResult
        {
            City = "Thành phố Hồ Chí Minh",
            District = "Phường Bến Nghé",
            AddressLine = $"Khu vực toạ độ ({latitude:F4}, {longitude:F4})",
            FormattedAddress = $"Khu vực toạ độ ({latitude:F4}, {longitude:F4}), Thành phố Hồ Chí Minh",
            Latitude = latitude,
            Longitude = longitude,
        });
    }
}
