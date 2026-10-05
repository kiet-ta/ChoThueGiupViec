using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Services;

/// <summary>
/// Real IGeoService calculating great-circle distances using the Haversine formula.
/// Mean Earth radius: 6,371,000 meters.
/// </summary>
public sealed class HaversineGeoService : IGeoService
{
    private const double EarthRadiusMeters = 6_371_000.0;

    public double DistanceMeters(GeoPoint from, GeoPoint to)
    {
        if (double.IsNaN(from.Latitude) || double.IsNaN(from.Longitude) ||
            double.IsNaN(to.Latitude) || double.IsNaN(to.Longitude))
        {
            throw new ArgumentException("GeoPoint coordinates cannot be NaN.");
        }

        var lat1Rad = DegreesToRadians(from.Latitude);
        var lat2Rad = DegreesToRadians(to.Latitude);
        var deltaLatRad = DegreesToRadians(to.Latitude - from.Latitude);
        var deltaLonRad = DegreesToRadians(to.Longitude - from.Longitude);

        var a = Math.Sin(deltaLatRad / 2.0) * Math.Sin(deltaLatRad / 2.0) +
                Math.Cos(lat1Rad) * Math.Cos(lat2Rad) *
                Math.Sin(deltaLonRad / 2.0) * Math.Sin(deltaLonRad / 2.0);

        var c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));

        return EarthRadiusMeters * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
}
