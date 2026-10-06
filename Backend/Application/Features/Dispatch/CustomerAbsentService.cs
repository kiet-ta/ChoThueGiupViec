using CommonService.Application.Common.Options;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Dispatch;

/// <summary>
/// Result of reporting a customer absent (BR-05, Q10).
/// </summary>
public sealed record CustomerAbsentReportResult
{
    public required bool Success { get; init; }
    public required long AssignmentId { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime? ReportedAtUtc { get; init; }
    public int CallAttempts { get; init; }
    public int ElapsedMinutes { get; init; }
    public decimal WorkerFeeRate { get; init; }
    public decimal AbsenceFeeAmount { get; init; }
    public decimal CustomerRefundRate { get; init; }
    public decimal CustomerRefundAmount { get; init; }
}

/// <summary>
/// Data abstraction for Customer Absent operations on CheckInLog and JobAssignment.
/// </summary>
public interface ICustomerAbsentRepository
{
    Task<JobAssignment?> GetAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default);
    Task<CheckInLog?> GetCheckInLogAsync(long assignmentId, CancellationToken cancellationToken = default);
    Task<CheckInLog> SaveCheckInLogAsync(CheckInLog log, CancellationToken cancellationToken = default);
    Task<bool> UpdateAssignmentAbsentFeeAsync(long assignmentId, decimal absenceFeeAmount, CancellationToken cancellationToken = default);
    Task<bool> RecordCallAttemptAsync(long assignmentId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Manages customer absent protocol:
/// - Logging system call attempts (Q17).
/// - Enforcing 15min wait &amp; &gt;= 2 call attempts criteria (BR-05, Q10).
/// - Publishing <see cref="CustomerAbsentReported"/> domain event for Admin review (M6).
/// </summary>
public class CustomerAbsentService
{
    private readonly ICustomerAbsentRepository _repository;
    private readonly IClock _clock;
    private readonly IMediator _mediator;
    private readonly BusinessRules _rules;
    private readonly ILogger<CustomerAbsentService> _logger;

    public CustomerAbsentService(
        ICustomerAbsentRepository repository,
        IClock clock,
        IMediator mediator,
        IOptions<BusinessRules> rules,
        ILogger<CustomerAbsentService>? logger = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _rules = rules?.Value ?? new BusinessRules();
        _logger = logger ?? NullLogger<CustomerAbsentService>.Instance;
    }

    /// <summary>
    /// Worker triggers system phone call attempt to contact the customer (Q17).
    /// Increments <see cref="CheckInLog.CallAttempts"/> by 1.
    /// </summary>
    public async Task<bool> RecordCallAttemptAsync(long assignmentId, int workerId, CancellationToken cancellationToken = default)
    {
        var assignment = await _repository.GetAssignmentAsync(assignmentId, cancellationToken);
        if (assignment == null || (assignment.WorkerId != workerId && assignment.AgencyId == null))
        {
            return false;
        }

        if (assignment.AssignmentStatus != JobAssignmentStatus.CheckedIn)
        {
            _logger.LogWarning("Cannot record call attempt for assignment {AssignmentId}: not in CHECKED_IN status.", assignmentId);
            return false;
        }

        return await _repository.RecordCallAttemptAsync(assignmentId, cancellationToken);
    }

    /// <summary>
    /// Worker submits report of customer absence after waiting >= 15 minutes and >= 2 call attempts (BR-05, Q10).
    /// </summary>
    public async Task<CustomerAbsentReportResult> ReportCustomerAbsentAsync(
        long assignmentId,
        int workerId,
        CancellationToken cancellationToken = default)
    {
        var assignment = await _repository.GetAssignmentAsync(assignmentId, cancellationToken);
        if (assignment == null)
        {
            return new CustomerAbsentReportResult
            {
                Success = false,
                AssignmentId = assignmentId,
                ErrorMessage = "Không tìm thấy ca làm việc."
            };
        }

        if (assignment.WorkerId != workerId && assignment.AgencyId == null)
        {
            return new CustomerAbsentReportResult
            {
                Success = false,
                AssignmentId = assignmentId,
                ErrorMessage = "Ca làm việc không thuộc về thợ này."
            };
        }

        if (assignment.AssignmentStatus != JobAssignmentStatus.CheckedIn)
        {
            return new CustomerAbsentReportResult
            {
                Success = false,
                AssignmentId = assignmentId,
                ErrorMessage = $"Ca làm việc đang ở trạng thái {assignment.AssignmentStatus}. Phải hoàn thành check-in hiện trường trước khi báo vắng mặt."
            };
        }

        var log = await _repository.GetCheckInLogAsync(assignmentId, cancellationToken);
        if (log == null)
        {
            return new CustomerAbsentReportResult
            {
                Success = false,
                AssignmentId = assignmentId,
                ErrorMessage = "Chưa có dữ liệu check-in hiện trường."
            };
        }

        var nowUtc = _clock.UtcNow;
        var elapsed = nowUtc - log.CheckedInAt;
        int minWaitMinutes = _rules.Absence.MinWaitMinutes > 0 ? _rules.Absence.MinWaitMinutes : 15;
        int minCalls = _rules.Absence.MinCallAttempts > 0 ? _rules.Absence.MinCallAttempts : 2;

        // Validation Rule 1: Wait time >= 15 minutes
        if (elapsed.TotalMinutes < minWaitMinutes)
        {
            int remainingMins = (int)Math.Ceiling(minWaitMinutes - elapsed.TotalMinutes);
            return new CustomerAbsentReportResult
            {
                Success = false,
                AssignmentId = assignmentId,
                CallAttempts = log.CallAttempts,
                ElapsedMinutes = (int)elapsed.TotalMinutes,
                ErrorMessage = $"Chưa đủ thời gian chờ tối thiểu {minWaitMinutes} phút (hiện tại: {(int)elapsed.TotalMinutes} phút, còn thiếu {remainingMins} phút) (BR-05)."
            };
        }

        // Validation Rule 2: Minimum call attempts >= 2
        if (log.CallAttempts < minCalls)
        {
            return new CustomerAbsentReportResult
            {
                Success = false,
                AssignmentId = assignmentId,
                CallAttempts = log.CallAttempts,
                ElapsedMinutes = (int)elapsed.TotalMinutes,
                ErrorMessage = $"Cần thực hiện tối thiểu {minCalls} cuộc gọi hệ thống trước khi báo vắng mặt (hiện tại: {log.CallAttempts} cuộc) (BR-05, Q10)."
            };
        }

        // Record customer_absent_at in CheckInLog
        log.CustomerAbsentAt = nowUtc;
        await _repository.SaveCheckInLogAsync(log, cancellationToken);

        // Calculate 40% compensation fee for worker and 60% refund for customer (Q10)
        decimal feeRate = _rules.Absence.FeeRate > 0 ? _rules.Absence.FeeRate : 0.40m;
        decimal absenceFeeAmount = Math.Round(assignment.GrossAmount * feeRate, 2);
        decimal refundRate = 1.0m - feeRate;
        decimal refundAmount = Math.Round(assignment.GrossAmount * refundRate, 2);

        await _repository.UpdateAssignmentAbsentFeeAsync(assignmentId, absenceFeeAmount, cancellationToken);

        // Publish CustomerAbsentReported domain event for Admin review (M6)
        await _mediator.Publish(new CustomerAbsentReported(
            AssignmentId: assignmentId,
            OrderId: assignment.OrderId,
            WorkerId: assignment.WorkerId,
            ReportedAtUtc: nowUtc
        ), cancellationToken);

        _logger.LogInformation(
            "Worker {WorkerId} reported customer absent for Assignment {AssignmentId} (Elapsed: {Elapsed}m, Calls: {Calls}). Fee: {Fee} (40%).",
            workerId, assignmentId, (int)elapsed.TotalMinutes, log.CallAttempts, absenceFeeAmount);

        return new CustomerAbsentReportResult
        {
            Success = true,
            AssignmentId = assignmentId,
            ReportedAtUtc = nowUtc,
            CallAttempts = log.CallAttempts,
            ElapsedMinutes = (int)elapsed.TotalMinutes,
            WorkerFeeRate = feeRate,
            AbsenceFeeAmount = absenceFeeAmount,
            CustomerRefundRate = refundRate,
            CustomerRefundAmount = refundAmount
        };
    }
}
