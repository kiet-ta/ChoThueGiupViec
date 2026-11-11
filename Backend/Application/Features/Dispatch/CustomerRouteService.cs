using CommonService.Application.Exceptions;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Dispatch;

/// <summary>A point on the customer's map.</summary>
public record RoutePointDto(double Latitude, double Longitude);

/// <summary>
/// The route from the worker to the customer's house (ROUTE-WEB-01, #765). <c>WorkerPosition</c> is rounded to 3 decimals
/// (about 100 m) and the line starts there, so the worker's exact position is never exposed.
/// </summary>
public record CustomerRouteDto(
    long AssignmentId,
    double DistanceMeters,
    double DurationSeconds,
    string Polyline,
    RoutePointDto Destination,
    RoutePointDto WorkerPosition);

/// <summary>Error codes in <c>data.code</c> of a 409, so the web branches on the code, not the message.</summary>
public static class RouteErrorCodes
{
    public const string NotOnTheWay = "ROUTE_NOT_AVAILABLE";
    public const string WorkerLocationUnknown = "WORKER_LOCATION_UNKNOWN";
}

/// <summary>
/// Customer side of the route (policy CustomerOnly). Only the customer who owns the assignment can read it (another customer's
/// assignment is a 404) and only while the assignment is ASSIGNED, i.e. the worker is on the way.
/// </summary>
public class CustomerRouteService(
    IWorkerDispatchQuery dispatchQuery,
    IRoutingService routing,
    ICurrentUser currentUser)
{
    /// <summary>Decimals kept of the worker's position (3 = about 110 m).</summary>
    public const int WorkerPositionDecimals = 3;

    private int CustomerId => currentUser.UserId is int id && currentUser.Role == UserRole.Customer
        ? id
        : throw new ForbiddenAccessException("Authenticated customer user ID is required.");

    /// <summary>The route, or null when the routing engine cannot answer (the endpoint then answers 502).</summary>
    public async Task<CustomerRouteDto?> GetAsync(long assignmentId, CancellationToken cancellationToken = default)
    {
        var customerId = CustomerId;
        var view = await dispatchQuery.GetOfferViewAsync(assignmentId, cancellationToken);
        if (view is null || view.Assignment.CustomerId != customerId)
        {
            throw new NotFoundException("Assignment", assignmentId);
        }

        if (view.Assignment.AssignmentStatus != JobAssignmentStatus.Assigned)
        {
            throw new BusinessRuleViolationException(
                "The route is shown only while the worker is on the way.", RouteErrorCodes.NotOnTheWay);
        }

        if (view.WorkerLocation is not { } worker)
        {
            throw new BusinessRuleViolationException(
                "The worker has not shared a position yet.", RouteErrorCodes.WorkerLocationUnknown);
        }

        var from = new RoutePointDto(
            Math.Round(worker.Latitude, WorkerPositionDecimals),
            Math.Round(worker.Longitude, WorkerPositionDecimals));
        var to = new RoutePointDto(view.OrderLocation.Latitude, view.OrderLocation.Longitude);

        var route = await routing.GetRouteAsync(from.Latitude, from.Longitude, to.Latitude, to.Longitude, cancellationToken);
        return route is null
            ? null
            : new CustomerRouteDto(assignmentId, route.DistanceMeters, route.DurationSeconds, route.Polyline, to, from);
    }
}
