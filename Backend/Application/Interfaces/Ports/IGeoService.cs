namespace CommonService.Application.Interfaces.Ports;

/// <summary>A WGS84 coordinate in degrees.</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude);

/// <summary>GPS maths shared by dispatch (radius 5/7/10 km) and check-in (tolerance 100 m).</summary>
public interface IGeoService
{
    /// <summary>Great-circle distance between two points, in meters.</summary>
    double DistanceMeters(GeoPoint from, GeoPoint to);
}
