using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Admin;

/// <summary>
/// One absence report as the repository reads it: the check-in row of the assignment plus the names. <c>Rejected</c> is true when the
/// audit log holds a rejection for the assignment (question A3).
/// </summary>
public sealed record AbsenceReportRow(
    long AssignmentId,
    long OrderId,
    string OrderCode,
    int WorkerId,
    string WorkerName,
    WorkerType WorkerType,
    string? AgencyName,
    string CustomerName,
    JobAssignmentStatus AssignmentStatus,
    decimal GrossAmount,
    decimal? AbsenceFeeAmount,
    DateTime CheckedInAt,
    DateTime CustomerAbsentAt,
    bool GpsVerified,
    decimal DistanceM,
    decimal DeviceLat,
    decimal DeviceLng,
    int CallAttempts,
    string? PhotoUrl,
    bool Rejected);

/// <summary>
/// Read model and the one write of the absence approval (BE-M6-03). Reads <c>CHECK_IN_LOG</c>, <c>JOB_ASSIGNMENT</c>, <c>JOB_ORDER</c> and the
/// name columns of Customer, Worker and PartnerAgency read-only (contract question A6: no read port exists yet).
/// </summary>
public interface IAbsenceRepository
{
    /// <summary>Reports of one status (PENDING, APPROVED or REJECTED), oldest <c>customer_absent_at</c> first. <paramref name="page"/> is 1-based.</summary>
    Task<(IReadOnlyList<AbsenceReportRow> Items, int Total)> SearchAsync(
        string status, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>The report of an assignment; null when it has no check-in row with <c>customer_absent_at</c>.</summary>
    Task<AbsenceReportRow?> GetAsync(long assignmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the assignment from <c>CHECKED_IN</c> to <c>ABSENT</c> and stores the fee with ONE conditional update; false when it is not
    /// <c>CHECKED_IN</c> any more (somebody else decided first). The caller runs this inside its unit of work.
    /// </summary>
    Task<bool> TryMarkAbsentAsync(long assignmentId, decimal absenceFeeAmount, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Ids of the Admin accounts that are active, to be told about a new report (BE-M6-03b).</summary>
    Task<IReadOnlyList<int>> GetActiveAdminIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>The customer of an order; null when the order is unknown (BE-M6-03b).</summary>
    Task<int?> GetCustomerIdOfOrderAsync(long orderId, CancellationToken cancellationToken = default);
}
