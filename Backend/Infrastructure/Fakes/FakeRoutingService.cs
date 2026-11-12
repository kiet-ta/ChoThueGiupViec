using System.Text;
using CommonService.Application.Features.Dispatch;

namespace CommonService.Infrastructure.Fakes;

/// <summary>
/// Offline routing for Development and CI (no Valhalla needed): a straight line between the two points, with the distance from
/// the Haversine formula and a travel time at a fixed 25 km/h city speed. Deterministic.
/// </summary>
public class FakeRoutingService : IRoutingService
{
    public const double AverageSpeedKmh = 25.0;
    private const double EarthRadiusMeters = 6_371_000.0;

    public Task<RouteResult?> GetRouteAsync(double fromLat, double fromLng, double toLat, double toLng, CancellationToken ct = default)
    {
        var meters = HaversineMeters(fromLat, fromLng, toLat, toLng);
        var seconds = meters / (AverageSpeedKmh * 1000.0 / 3600.0);
        var polyline = EncodePolyline6([(fromLat, fromLng), (toLat, toLng)]);
        return Task.FromResult<RouteResult?>(new RouteResult(Math.Round(meters, 1), Math.Round(seconds, 1), polyline));
    }

    public Task<IReadOnlyList<double?>?> GetRoadDistancesAsync(
        double fromLat, double fromLng, IReadOnlyList<(double Lat, double Lng)> targets, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<double?>?>(
            targets.Select(t => (double?)Math.Round(HaversineMeters(fromLat, fromLng, t.Lat, t.Lng), 1)).ToList());

    public static double HaversineMeters(double lat1, double lng1, double lat2, double lng2)
    {
        static double Rad(double d) => d * Math.PI / 180.0;
        var dLat = Rad(lat2 - lat1);
        var dLng = Rad(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
    }

    /// <summary>Encodes points as a Google/Valhalla polyline with 6 decimals of precision.</summary>
    public static string EncodePolyline6(IEnumerable<(double Lat, double Lng)> points)
    {
        var sb = new StringBuilder();
        long prevLat = 0, prevLng = 0;
        foreach (var (lat, lng) in points)
        {
            var la = (long)Math.Round(lat * 1e6);
            var lo = (long)Math.Round(lng * 1e6);
            EncodeValue(la - prevLat, sb);
            EncodeValue(lo - prevLng, sb);
            prevLat = la;
            prevLng = lo;
        }

        return sb.ToString();
    }

    private static void EncodeValue(long value, StringBuilder sb)
    {
        var v = value < 0 ? ~(value << 1) : value << 1;
        while (v >= 0x20)
        {
            sb.Append((char)((0x20 | (int)(v & 0x1f)) + 63));
            v >>= 5;
        }

        sb.Append((char)((int)v + 63));
    }
}
