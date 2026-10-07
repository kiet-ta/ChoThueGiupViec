using System.Globalization;
using CommonService.Application.Common.Options;
using CommonService.Application.Features.Admin.Dtos;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Domain.StateMachines;
using CommonService.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Admin.Services;

public sealed class AbsenceResult<T>
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public IDictionary<string, string[]>? ValidationErrors { get; init; }

    /// <summary>Why an approval was refused (a 409), see <see cref="AbsenceConstants"/>.</summary>
    public IReadOnlyList<string>? BlockReasons { get; init; }

    public T? Data { get; init; }

    public static AbsenceResult<T> Ok(T data) => new() { Success = true, StatusCode = 200, Data = data };

    public static AbsenceResult<T> ValidationError(IDictionary<string, string[]> errors) => new()
    {
        StatusCode = 400,
        ErrorMessage = "Validation failed",
        ValidationErrors = errors,
    };

    public static AbsenceResult<T> NotFound() => new() { StatusCode = 404, ErrorMessage = "Absence report not found." };

    public static AbsenceResult<T> Conflict(string message) => new() { StatusCode = 409, ErrorMessage = message };

    public static AbsenceResult<T> Blocked(IReadOnlyList<string> reasons) => new()
    {
        StatusCode = 409,
        ErrorMessage = "The absence cannot be approved yet.",
        BlockReasons = reasons,
    };

    /// <summary>The refund port refused or failed; nothing was saved.</summary>
    public static AbsenceResult<T> BadGateway(string message) => new() { StatusCode = 502, ErrorMessage = message };
}

public interface IAbsenceReportService
{
    /// <summary>Every argument is the raw query-string text so a bad value is a 400 with field messages (decision O5).</summary>
    Task<AbsenceResult<AbsenceReportPageDto>> SearchAsync(
        string? status, string? page, string? pageSize, CancellationToken cancellationToken = default);

    Task<AbsenceResult<AbsenceReportDto>> GetAsync(long assignmentId, CancellationToken cancellationToken = default);

    Task<AbsenceResult<AbsenceReportDto>> ApproveAsync(int adminId, long assignmentId, CancellationToken cancellationToken = default);

    Task<AbsenceResult<AbsenceReportDto>> RejectAsync(int adminId, long assignmentId, string? reason, CancellationToken cancellationToken = default);
}

/// <summary>
/// Admin queue and decision on "customer absent" reports (BR-05, decision Q10; contract admin.md 2.2, BE-M6-03). An approval runs in
/// ONE unit of work: the assignment goes CHECKED_IN to ABSENT with the 40 % fee, the 60 % is refunded, the decision is audited; then
/// <see cref="CustomerAbsentApproved"/> is published.
/// </summary>
public sealed class AbsenceReportService(
    IAbsenceRepository reports,
    IRefundService refunds,
    IAuditLog audit,
    IUnitOfWork unitOfWork,
    IClock clock,
    IPublisher publisher,
    IOptions<BusinessRules> rules) : IAbsenceReportService
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Thrown inside the unit of work to undo it; never leaves this class.</summary>
    private sealed class ApprovalAbortedException(string message) : Exception(message);

    public async Task<AbsenceResult<AbsenceReportPageDto>> SearchAsync(
        string? status, string? page, string? pageSize, CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();

        var wanted = string.IsNullOrWhiteSpace(status) ? AbsenceConstants.Pending : status.Trim().ToUpperInvariant();
        if (!AbsenceConstants.Statuses.Contains(wanted))
        {
            errors["status"] = [$"Status must be one of: {string.Join(", ", AbsenceConstants.Statuses)}."];
        }

        var pageNumber = ParseInt(page, 1, 1, int.MaxValue, "page", "Page must be an integer of at least 1.", errors);
        var size = ParseInt(pageSize, DefaultPageSize, 1, MaxPageSize, "pageSize",
            $"Page size must be an integer between 1 and {MaxPageSize}.", errors);
        if (errors.Count > 0) return AbsenceResult<AbsenceReportPageDto>.ValidationError(errors);

        var (rows, total) = await reports.SearchAsync(wanted, pageNumber, size, cancellationToken);
        var now = clock.UtcNow;
        return AbsenceResult<AbsenceReportPageDto>.Ok(new AbsenceReportPageDto
        {
            Items = rows.Select(r => ToDto(r, now)).ToList(),
            Page = pageNumber,
            PageSize = size,
            Total = total,
        });
    }

    public async Task<AbsenceResult<AbsenceReportDto>> GetAsync(long assignmentId, CancellationToken cancellationToken = default)
    {
        var row = await reports.GetAsync(assignmentId, cancellationToken);
        return row is null ? AbsenceResult<AbsenceReportDto>.NotFound() : AbsenceResult<AbsenceReportDto>.Ok(ToDto(row, clock.UtcNow));
    }

    public async Task<AbsenceResult<AbsenceReportDto>> ApproveAsync(int adminId, long assignmentId, CancellationToken cancellationToken = default)
    {
        var row = await reports.GetAsync(assignmentId, cancellationToken);
        if (row is null) return AbsenceResult<AbsenceReportDto>.NotFound();
        if (StatusOf(row) != AbsenceConstants.Pending) return AbsenceResult<AbsenceReportDto>.Conflict("The report is already decided.");

        var now = clock.UtcNow;
        var block = BlockReasons(row, now);
        if (block.Count > 0) return AbsenceResult<AbsenceReportDto>.Blocked(block);

        // Reached only for CHECKED_IN: the other states are decided (ABSENT) or cannot go to ABSENT; the machine stays the authority.
        if (!JobAssignmentStateMachine.Instance.CanTransition(row.AssignmentStatus, JobAssignmentStatus.Absent))
        {
            return AbsenceResult<AbsenceReportDto>.Conflict($"The assignment is {DbEnum.ToDb(row.AssignmentStatus)}, not CHECKED_IN.");
        }

        var fee = Fee(row.GrossAmount);
        var refund = row.GrossAmount - fee;

        try
        {
            var done = await unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                if (!await reports.TryMarkAbsentAsync(assignmentId, fee, now, cancellationToken)) return false; // decided between our read and now

                if (refund > 0)
                {
                    var result = await refunds.RefundAsync(
                        new RefundRequest(row.OrderId, refund, $"Customer absent approved for assignment {assignmentId}"), cancellationToken);
                    if (!result.Succeeded) throw new ApprovalAbortedException(result.Message ?? "The refund was refused.");
                }

                await audit.WriteAsync(
                    new AuditEntry(AuditActorType.Admin, adminId, AbsenceConstants.AuditEntityType, assignmentId.ToString(),
                        AbsenceConstants.AuditFieldName, AbsenceConstants.Pending, AbsenceConstants.Approved,
                        string.Create(CultureInfo.InvariantCulture, $"Customer absent approved: fee {fee:0}, refund {refund:0}")),
                    cancellationToken);
                return true;
            }, cancellationToken);

            if (!done) return AbsenceResult<AbsenceReportDto>.Conflict("The report is already decided.");
        }
        catch (ApprovalAbortedException ex)
        {
            return AbsenceResult<AbsenceReportDto>.BadGateway(ex.Message);
        }

        await publisher.Publish(new CustomerAbsentApproved(assignmentId, row.OrderId, row.WorkerId, fee, refund, now), cancellationToken);

        var approved = await reports.GetAsync(assignmentId, cancellationToken) ?? row;
        return AbsenceResult<AbsenceReportDto>.Ok(ToDto(approved, now));
    }

    public async Task<AbsenceResult<AbsenceReportDto>> RejectAsync(
        int adminId, long assignmentId, string? reason, CancellationToken cancellationToken = default)
    {
        var clean = (reason ?? string.Empty).Trim();
        var errors = new Dictionary<string, string[]>();
        if (clean.Length == 0) errors["reason"] = ["A reason is required."];
        else if (clean.Length > AbsenceConstants.MaxReasonLength) errors["reason"] = [$"The reason must be at most {AbsenceConstants.MaxReasonLength} characters."];
        if (errors.Count > 0) return AbsenceResult<AbsenceReportDto>.ValidationError(errors);

        var row = await reports.GetAsync(assignmentId, cancellationToken);
        if (row is null) return AbsenceResult<AbsenceReportDto>.NotFound();
        if (StatusOf(row) != AbsenceConstants.Pending) return AbsenceResult<AbsenceReportDto>.Conflict("The report is already decided.");

        // Question A4: the rejection is only recorded; the assignment is left as it is (M3 owns what happens next).
        await audit.WriteAsync(
            new AuditEntry(AuditActorType.Admin, adminId, AbsenceConstants.AuditEntityType, assignmentId.ToString(),
                AbsenceConstants.AuditFieldName, AbsenceConstants.Pending, AbsenceConstants.Rejected, clean),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var rejected = await reports.GetAsync(assignmentId, cancellationToken) ?? row;
        return AbsenceResult<AbsenceReportDto>.Ok(ToDto(rejected, clock.UtcNow));
    }

    /// <summary>Whole VND, half away from zero (decision G-2): the worker's share of the order.</summary>
    private decimal Fee(decimal gross) => Vnd.Round(gross * rules.Value.Absence.FeeRate);

    /// <summary>ABSENT wins over a late rejection row (the approval already moved the money).</summary>
    private static string StatusOf(AbsenceReportRow row) =>
        row.AssignmentStatus == JobAssignmentStatus.Absent ? AbsenceConstants.Approved
        : row.Rejected ? AbsenceConstants.Rejected
        : AbsenceConstants.Pending;

    /// <summary>The Q10 conditions that do not hold yet; empty when the approval may go ahead.</summary>
    private List<string> BlockReasons(AbsenceReportRow row, DateTime nowUtc)
    {
        var absence = rules.Value.Absence;
        var reasons = new List<string>();
        if (!row.GpsVerified) reasons.Add(AbsenceConstants.GpsNotVerified);
        if (row.CallAttempts < absence.MinCallAttempts) reasons.Add(AbsenceConstants.CallsBelowMinimum);
        if (nowUtc - row.CheckedInAt < TimeSpan.FromMinutes(absence.MinWaitMinutes)) reasons.Add(AbsenceConstants.WaitBelowMinimum);
        // Rows come from check-ins with customer_absent_at set; the reason stays so the contract list is complete if that ever changes.
        if (row.CustomerAbsentAt == default) reasons.Add(AbsenceConstants.AbsenceNotReported);
        return reasons;
    }

    private AbsenceReportDto ToDto(AbsenceReportRow row, DateTime nowUtc)
    {
        var status = StatusOf(row);
        var pending = status == AbsenceConstants.Pending;
        var block = pending ? BlockReasons(row, nowUtc) : [];
        var fee = status == AbsenceConstants.Approved && row.AbsenceFeeAmount is { } stored ? stored : Fee(row.GrossAmount);
        var waitedUntil = pending ? nowUtc : row.CustomerAbsentAt;

        return new AbsenceReportDto
        {
            AssignmentId = row.AssignmentId,
            OrderId = row.OrderId,
            OrderCode = row.OrderCode,
            WorkerId = row.WorkerId,
            WorkerName = row.WorkerName,
            WorkerType = DbEnum.ToDb(row.WorkerType),
            AgencyName = row.AgencyName,
            CustomerName = row.CustomerName,
            Status = status,
            CheckedInAt = Utc(row.CheckedInAt),
            CustomerAbsentAt = Utc(row.CustomerAbsentAt),
            GpsVerified = row.GpsVerified,
            DistanceM = row.DistanceM,
            DeviceLat = row.DeviceLat,
            DeviceLng = row.DeviceLng,
            CallAttempts = row.CallAttempts,
            WaitedMinutes = Math.Max(0, (int)Math.Floor((waitedUntil - row.CheckedInAt).TotalMinutes)),
            GrossAmount = row.GrossAmount,
            AbsenceFeeAmount = fee,
            CustomerRefundAmount = row.GrossAmount - fee,
            PhotoUrl = row.PhotoUrl,
            CanApprove = pending && block.Count == 0,
            BlockReasons = block,
        };
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static int ParseInt(string? raw, int fallback, int min, int max, string field, string message, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        if (int.TryParse(raw.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max)
        {
            return value;
        }

        errors[field] = [message];
        return fallback;
    }
}
