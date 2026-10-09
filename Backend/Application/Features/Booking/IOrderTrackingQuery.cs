using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Booking;

/// <summary>One JOB_ASSIGNMENT row of an order (BE-M2-10). Every row, whatever its status: hiding OFFERED / CANCELLED / REASSIGNED from the customer is the endpoint's rule (booking.md 2, Q22 D1).</summary>
public sealed record AssignmentProgress(
    long AssignmentId,
    byte AssignmentSeq,
    int WorkerId,
    JobAssignmentStatus AssignmentStatus,
    DateTime? AcceptedAt,
    DateTime? CompletedAt);

/// <summary>One JOB_ASSIGNMENT row of a customer (BE-M2-10).</summary>
public sealed record CustomerAssignmentSummary(
    long AssignmentId,
    long OrderId,
    byte AssignmentSeq,
    ServiceTier ServiceTier,
    JobAssignmentStatus AssignmentStatus,
    decimal GrossAmount,
    DateTime CreatedAt,
    DateTime? CompletedAt);

/// <summary>
/// Booking reads of the order tracking views (PRD 5.2): each query scans JOB_ASSIGNMENT only (0 JOIN),
/// by the denormalized order_id / customer_id. Ownership checks belong to the caller.
/// </summary>
public interface IOrderTrackingQuery
{
    /// <summary>Assignments of the order, ordered by assignment_seq then id. Empty when none (or no such order).</summary>
    Task<IReadOnlyList<AssignmentProgress>> GetOrderProgressAsync(long orderId, CancellationToken cancellationToken = default);

    /// <summary>Assignments of the customer, newest created_at first (ties: highest id first). Empty when none.</summary>
    Task<IReadOnlyList<CustomerAssignmentSummary>> GetCustomerHistoryAsync(int customerId, CancellationToken cancellationToken = default);
}
