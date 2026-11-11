namespace CommonService.Application.Features.Dispatch;

/// <summary>A driving route between two points (ROUTE-WEB-01, #765).</summary>
/// <param name="DistanceMeters">Road distance in metres.</param>
/// <param name="DurationSeconds">Travel time in seconds.</param>
/// <param name="Polyline">The route line as an encoded polyline with 6 decimals of precision (the format Valhalla returns).</param>
public record RouteResult(double DistanceMeters, double DurationSeconds, string Polyline);

/// <summary>
/// Port for road routing (lives with the Dispatch feature, not in the shared port catalogue, because only the route endpoint uses it). Implemented by <c>ValhallaRoutingService</c> (self-hosted Valhalla on OpenStreetMap data, costing
/// <c>motor_scooter</c>); with no <c>Valhalla:BaseUrl</c> configured it answers with <c>FakeRoutingService</c> (straight line).
/// Returns null when the routing engine cannot answer, so the caller can degrade instead of failing.
/// </summary>
public interface IRoutingService
{
    Task<RouteResult?> GetRouteAsync(double fromLat, double fromLng, double toLat, double toLng, CancellationToken ct = default);

    /// <summary>
    /// Up to <paramref name="maxRoutes"/> routes, the fastest first, the others sorted by duration (ROUTE-MOB-01, #760). Empty when the
    /// engine cannot answer. The default is the single route of <see cref="GetRouteAsync"/>, so an adapter without alternatives needs nothing.
    /// </summary>
    async Task<IReadOnlyList<RouteResult>> GetRoutesAsync(
        double fromLat, double fromLng, double toLat, double toLng, int maxRoutes, CancellationToken ct = default)
    {
        var one = await GetRouteAsync(fromLat, fromLng, toLat, toLng, ct);
        return one is null ? [] : [one];
    }
}
