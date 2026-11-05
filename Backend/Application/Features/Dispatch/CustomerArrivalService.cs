using CommonService.Application.Exceptions;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Dispatch;

/// <summary>
/// The customer's view of a check-in (CUST-ARRIVAL-01, #657). <c>AwaitingCustomerConfirmation</c> is true when the worker tried to
/// check in, the GPS was more than the tolerance off (BR-04) and nobody resolved it yet: the customer may confirm the arrival.
/// </summary>
public record ArrivalStatusDto(
    long AssignmentId,
    string Status,
    bool AwaitingCustomerConfirmation,
    decimal? DistanceMeters,
    DateTime? AttemptedAt);

/// <summary>Answer of the customer confirmation (contract dispatch.md 3.2, <c>check-in/confirm</c>).</summary>
public record ArrivalConfirmedDto(long AssignmentId, bool IsCustomerConfirmed, string Status);

/// <summary>
/// Customer side of the BR-04 fallback (policy CustomerOnly): the worker's GPS was off by more than the tolerance, so the customer
/// confirms that the worker is at the door. An assignment that is not the caller's is a 404 (never reveal another customer's job);
/// an assignment that does not wait for a confirmation is a 409 <c>ARRIVAL_NOT_PENDING</c>.
/// </summary>
public class CustomerArrivalService(
    IFieldCheckInRepository repository,
    FieldCheckInService checkIn,
    ICurrentUser currentUser)
{
    /// <summary>
    /// The <c>fallback_method</c> value stored for a customer confirmation. The column is <c>varchar(20)</c>, so the longer
    /// <c>CUSTOMER_CONFIRMATION</c> (21 characters) of the older comments does not fit.
    /// </summary>
    public const string CustomerConfirmMethod = "CUSTOMER_CONFIRM";

    public const string ArrivalNotPending = "ARRIVAL_NOT_PENDING";

    private int CustomerId => currentUser.UserId is int id && currentUser.Role == UserRole.Customer
        ? id
        : throw new ForbiddenAccessException("Authenticated customer user ID is required.");

    public async Task<ArrivalStatusDto> GetAsync(long assignmentId, CancellationToken cancellationToken = default)
    {
        var assignment = await RequireOwnAsync(assignmentId, CustomerId, cancellationToken);
        var log = await repository.GetCheckInLogAsync(assignmentId, cancellationToken);
        var awaiting = IsAwaiting(assignment, log);
        return new ArrivalStatusDto(
            assignmentId,
            DbEnum.ToDb(assignment.AssignmentStatus),
            awaiting,
            log?.DistanceM,
            log?.CheckedInAt);
    }

    public async Task<ArrivalConfirmedDto> ConfirmAsync(long assignmentId, CancellationToken cancellationToken = default)
    {
        var assignment = await RequireOwnAsync(assignmentId, CustomerId, cancellationToken);
        var log = await repository.GetCheckInLogAsync(assignmentId, cancellationToken);
        if (!IsAwaiting(assignment, log))
        {
            throw new BusinessRuleViolationException(
                "The worker has not reported an arrival that needs your confirmation.", ArrivalNotPending);
        }

        var result = await checkIn.ResolveFallbackCheckInAsync(assignmentId, CustomerConfirmMethod, null, cancellationToken);
        if (!result.Success)
        {
            throw new BusinessRuleViolationException(
                result.ErrorMessage ?? "The arrival could not be confirmed.", ArrivalNotPending);
        }

        return new ArrivalConfirmedDto(assignmentId, IsCustomerConfirmed: true, DbEnum.ToDb(JobAssignmentStatus.CheckedIn));
    }

    /// <summary>A failed GPS attempt (BR-04) on an assignment that is still waiting for its check-in.</summary>
    public static bool IsAwaiting(JobAssignment assignment, CheckInLog? log) =>
        assignment.AssignmentStatus == JobAssignmentStatus.Assigned
        && log is { GpsVerified: false, FallbackMethod: null };

    private async Task<JobAssignment> RequireOwnAsync(long assignmentId, int customerId, CancellationToken cancellationToken)
    {
        var assignment = await repository.GetAssignmentAsync(assignmentId, cancellationToken);
        if (assignment == null || assignment.CustomerId != customerId)
        {
            throw new NotFoundException("Assignment", assignmentId);
        }

        return assignment;
    }
}
