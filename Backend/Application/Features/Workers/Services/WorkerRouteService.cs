using CommonService.Application.Exceptions;
using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Workers.Services;

/// <summary>One way to the house: road distance, travel time and the line as a 6-decimal encoded polyline.</summary>
public record WorkerRouteOption(double DistanceMeters, double DurationSeconds, string Polyline);

/// <summary>The routes from the worker to the customer's house (contract workers.md 2.3.5). The first one is the fastest.</summary>
public record WorkerRouteResponse(
    long AssignmentId,
    double DestinationLatitude,
    double DestinationLongitude,
    IReadOnlyList<WorkerRouteOption> Routes);

/// <summary>Error code in <c>data.code</c> of the 409 of the route endpoint.</summary>
public static class WorkerRouteErrorCodes
{
    public const string NotOnTheWay = "ROUTE_NOT_AVAILABLE";
}

/// <summary>
/// Routes for the signed-in worker (ROUTE-MOB-01, #760): up to 3 ways from the worker's current position to the house of one of the
/// worker's own jobs. The destination is always the order's address, never a value from the request, and only a job the worker has
/// accepted is visible (the same rule as the job detail, so the address is not revealed earlier).
/// </summary>
public class WorkerRouteService(
    IWorkerAssignmentRepository repository,
    IRoutingService routing,
    ICurrentUser currentUser)
{
    public const int MaxRoutes = 3;

    /// <summary>Jobs that still have a trip to make: the worker is on the way (ASSIGNED) or just arrived (CHECKED_IN).</summary>
    private static readonly JobAssignmentStatus[] Travelling = [JobAssignmentStatus.Assigned, JobAssignmentStatus.CheckedIn];

    private static readonly JobAssignmentStatus[] Booked =
    [
        JobAssignmentStatus.Assigned,
        JobAssignmentStatus.CheckedIn,
        JobAssignmentStatus.InProgress,
        JobAssignmentStatus.AwaitingAcceptance,
        JobAssignmentStatus.Completed,
    ];

    /// <summary>The routes, or null when the routing engine cannot answer (the endpoint then answers 502).</summary>
    public async Task<WorkerRouteResponse?> GetAsync(long assignmentId, double fromLat, double fromLng, CancellationToken ct = default)
    {
        if (currentUser.UserId is not int workerId)
        {
            throw new ForbiddenAccessException("Authenticated worker user ID is required.");
        }

        if (fromLat is < -90 or > 90 || fromLng is < -180 or > 180 || double.IsNaN(fromLat) || double.IsNaN(fromLng))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "fromLat", ["fromLat must be between -90 and 90 and fromLng between -180 and 180."] }
            });
        }

        var row = await repository.FindDetailForWorkerAsync(assignmentId, workerId, ct);
        if (row is null || !Booked.Contains(row.Status))
        {
            throw new NotFoundException($"Job assignment {assignmentId} not found.");
        }

        if (!Travelling.Contains(row.Status))
        {
            throw new BusinessRuleViolationException(
                "The route is shown only before the work starts.", WorkerRouteErrorCodes.NotOnTheWay);
        }

        var toLat = (double)row.Latitude;
        var toLng = (double)row.Longitude;
        var routes = await routing.GetRoutesAsync(fromLat, fromLng, toLat, toLng, MaxRoutes, ct);
        if (routes.Count == 0)
        {
            return null;
        }

        return new WorkerRouteResponse(
            assignmentId,
            toLat,
            toLng,
            routes.Select(r => new WorkerRouteOption(r.DistanceMeters, r.DurationSeconds, r.Polyline)).ToList());
    }
}
