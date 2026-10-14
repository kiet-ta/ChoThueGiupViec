using CommonService.Application.Exceptions;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Agencies;

/// <summary>One job of the agency (a <c>JOB_ASSIGNMENT</c> with its <c>agency_id</c>).</summary>
public sealed record AgencyJobDto(
    long AssignmentId, long OrderId, int WorkerId, string WorkerName, string ServiceTier, byte AssignmentSeq, string AssignmentStatus,
    decimal GrossAmount, decimal CommissionRate, decimal PayoutAmount, DateTime CreatedAt, DateTime? CompletedAt);

public sealed record AgencyJobPageDto(IReadOnlyList<AgencyJobDto> Items, int Page, int PageSize, int Total);

/// <summary>Per-worker performance of the period (Pro, contract agencies.md 8).</summary>
public sealed record WorkerPerformanceDto(
    int WorkerId, string WorkerName, int Assigned, int Completed, int Cancelled, int Absent, int Incident, decimal PayoutTotal, decimal CompletionRate);

public sealed record PerformanceReportDto(DateOnly From, DateOnly To, int TotalJobs, decimal PayoutTotal, IReadOnlyList<WorkerPerformanceDto> Workers);

/// <summary>Raw counts of one worker over a period, as the repository groups them in one query.</summary>
public sealed record WorkerPerformanceRow(int WorkerId, string WorkerName, int Assigned, int Completed, int Cancelled, int Absent, int Incident, decimal PayoutTotal);

public static class ReportErrorCodes
{
    public const string FeatureNotInPlan = "FEATURE_NOT_IN_PLAN";
}

/// <summary>Read side of the agency's jobs (BE-M5-08): every query filters on <c>agency_id</c> of the assignment, no JOIN (PRD 5.2).</summary>
public interface IAgencyReportRepository
{
    Task<(IReadOnlyList<AgencyJobRow> Items, int Total)> ListJobsAsync(
        int agencyId, JobAssignmentStatus? status, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>One row per worker with the counts of the assignments created in [fromUtc, toUtc).</summary>
    Task<IReadOnlyList<WorkerPerformanceRow>> PerformanceAsync(
        int agencyId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>The names of the workers by id (a second indexed read instead of a JOIN).</summary>
    Task<IReadOnlyDictionary<int, string>> GetWorkerNamesAsync(IReadOnlyCollection<int> workerIds, CancellationToken cancellationToken = default);
}

/// <summary>A job row without the worker name.</summary>
public sealed record AgencyJobRow(
    long AssignmentId, long OrderId, int WorkerId, ServiceTier ServiceTier, byte AssignmentSeq, JobAssignmentStatus AssignmentStatus,
    decimal GrossAmount, decimal CommissionRate, decimal PayoutAmount, DateTime CreatedAt, DateTime? CompletedAt);

/// <summary>
/// Agency queries (BE-M5-08, contract agencies.md 8): the company's jobs, and the per-worker performance report that only a package
/// with <c>has_analytics</c> (Pro, decisions Q08) may read. The Verified Partner label and the priority-dispatch flag are read from the
/// agency row and the package in force (<see cref="SubscriptionStateDto"/>).
/// </summary>
public sealed class AgencyReportService(
    IAgencyReportRepository repository,
    SubscriptionService subscriptions,
    ICurrentUser currentUser,
    IClock clock)
{
    /// <summary>provenance: a period longer than a year is not a performance report; also bounds the query.</summary>
    public const int MaxPeriodDays = 366;

    private int AgencyId => currentUser.UserId is int id && currentUser.Role == UserRole.Partner
        ? id
        : throw new ForbiddenAccessException("Authenticated partner agency is required.");

    public async Task<AgencyJobPageDto> ListJobsAsync(string? status, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var agencyId = AgencyId;
        JobAssignmentStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            parsed = Enum.GetValues<JobAssignmentStatus>()
                .Cast<JobAssignmentStatus?>()
                .FirstOrDefault(s => DbEnum.ToDb(s!.Value) == status.Trim().ToUpperInvariant())
                ?? throw new ValidationException("status", "status is not a known assignment status.");
        }

        var size = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 100);
        var current = Math.Max(1, page);
        var (rows, total) = await repository.ListJobsAsync(agencyId, parsed, (current - 1) * size, size, cancellationToken);
        var names = await repository.GetWorkerNamesAsync(rows.Select(r => r.WorkerId).Distinct().ToList(), cancellationToken);
        var items = rows.Select(r => new AgencyJobDto(
            r.AssignmentId, r.OrderId, r.WorkerId, names.TryGetValue(r.WorkerId, out var name) ? name : string.Empty,
            DbEnum.ToDb(r.ServiceTier), r.AssignmentSeq, DbEnum.ToDb(r.AssignmentStatus),
            r.GrossAmount, r.CommissionRate, r.PayoutAmount, r.CreatedAt, r.CompletedAt)).ToList();
        return new AgencyJobPageDto(items, current, size, total);
    }

    public async Task<PerformanceReportDto> GetPerformanceAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var agencyId = AgencyId;
        var effective = await subscriptions.GetEffectivePackageAsync(agencyId, cancellationToken);
        if (!effective.Package.HasAnalytics)
        {
            throw new BusinessRuleViolationException(
                "The performance report is part of the Pro package.", ReportErrorCodes.FeatureNotInPlan);
        }

        var today = clock.LocalToday;
        var end = to ?? today;
        var start = from ?? end.AddDays(-29);
        if (start > end || end.DayNumber - start.DayNumber >= MaxPeriodDays)
        {
            throw new ValidationException("from", $"from must not be after to and the period is at most {MaxPeriodDays} days.");
        }

        var fromUtc = clock.ToUtc(DateTime.SpecifyKind(start.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified));
        var toUtc = clock.ToUtc(DateTime.SpecifyKind(end.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified));
        var rows = await repository.PerformanceAsync(agencyId, fromUtc, toUtc, cancellationToken);

        var workers = rows
            .Select(r => new WorkerPerformanceDto(
                r.WorkerId, r.WorkerName, r.Assigned, r.Completed, r.Cancelled, r.Absent, r.Incident, r.PayoutTotal,
                r.Assigned == 0 ? 0m : Math.Round((decimal)r.Completed / r.Assigned, 3)))
            .OrderByDescending(w => w.Completed).ThenBy(w => w.WorkerId)
            .ToList();
        return new PerformanceReportDto(start, end, workers.Sum(w => w.Assigned), workers.Sum(w => w.PayoutTotal), workers);
    }
}
