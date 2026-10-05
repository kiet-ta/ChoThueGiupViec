using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Fakes;

/// <summary>Haversine distance, or a fixed value when a test wants one.</summary>
public sealed class FakeGeoService : IGeoService
{
    private const double EarthRadiusMeters = 6_371_000d;

    /// <summary>When set, every call returns this distance.</summary>
    public double? FixedDistanceMeters { get; set; }

    public double DistanceMeters(GeoPoint from, GeoPoint to)
    {
        if (FixedDistanceMeters is { } fixedDistance)
        {
            return fixedDistance;
        }

        var lat1 = ToRadians(from.Latitude);
        var lat2 = ToRadians(to.Latitude);
        var dLat = lat2 - lat1;
        var dLon = ToRadians(to.Longitude - from.Longitude);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1d, Math.Sqrt(a)));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
}
