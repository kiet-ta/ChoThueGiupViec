using CommonService.Application.Common.Models;
using CommonService.Application.Common.Helpers;
using CommonService.Application.Common.Options;
using Microsoft.Extensions.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Domain.ValueObjects;
using MediatR;

namespace CommonService.Application.Features.Workers.Services;

public class JobCompletionService(
    IWorkerRepository workerRepository,
    IPublisher publisher,
    ICurrentUser currentUser,
    IClock clock,
    IOptions<BusinessRules>? rules = null,
    IContinuationRepository? continuations = null) : IJobCompletionService
{
    private readonly AcceptanceRules _acceptance = rules?.Value.Acceptance ?? new AcceptanceRules();

    private const string WorkerNotAssigned = "Worker is not assigned to this job assignment.";
    private const string CustomerNotAuthorized = "Customer is not authorized for this assignment.";

    /// <summary>
    /// Fails closed: the caller must be authenticated, have the expected role and a positive integer id, otherwise the action is refused
    /// before the assignment is read. Ownership of the assignment is checked by the caller of this method.
    /// </summary>
    private int RequireCaller(UserRole role, string message)
    {
        if (!currentUser.IsAuthenticated || currentUser.Role != role || currentUser.UserId is not int id || id <= 0)
        {
            throw new ForbiddenAccessException(message);
        }

        return id;
    }

    public async Task<ApiResponse<AssignmentStatusResponse>> SubmitAsync(long assignmentId, string? notes, CancellationToken ct = default)
    {
        var workerId = RequireCaller(UserRole.Worker, WorkerNotAssigned);

        var assignment = await workerRepository.GetAssignmentByIdAsync(assignmentId, ct);
        if (assignment == null)
        {
            throw new NotFoundException($"Job assignment with ID {assignmentId} not found.");
        }

        if (assignment.WorkerId != workerId)
        {
            throw new ForbiddenAccessException(WorkerNotAssigned);
        }

        if (assignment.AssignmentStatus != JobAssignmentStatus.InProgress)
        {
            throw new BusinessRuleViolationException($"Cannot submit completion for assignment in status {assignment.AssignmentStatus}.");
        }

        // Validate Before & After photos per BR-06 & contract §2.4/§2.5.1
        var beforePhotos = await workerRepository.GetAcceptedJobPhotosByPhaseAsync(assignmentId, "BEFORE", ct);
        var afterPhotos = await workerRepository.GetAcceptedJobPhotosByPhaseAsync(assignmentId, "AFTER", ct);

        if (beforePhotos.Count < 3 || afterPhotos.Count < 3)
        {
            throw new ValidationException("photos", "At least 3 accepted BEFORE photos and 3 accepted AFTER photos are required.");
        }

        var beforeAngles = beforePhotos.Select(p => p.AngleNo).Distinct().OrderBy(a => a).ToList();
        var afterAngles = afterPhotos.Select(p => p.AngleNo).Distinct().OrderBy(a => a).ToList();

        if (!beforeAngles.SequenceEqual(afterAngles))
        {
            throw new ValidationException("angles", "AFTER photo angles do not match BEFORE photo angles.");
        }

        var nowUtc = clock.UtcNow;
        assignment.TransitionTo(JobAssignmentStatus.AwaitingAcceptance);
        assignment.UpdatedAt = nowUtc;
        // Decision D32: T, the start of the 24-hour acceptance window (a new window after every redo).
        assignment.SubmittedAt = nowUtc;
        assignment.AcceptanceRemindedAt = null;

        await workerRepository.SaveChangesAsync(ct);

        return ApiResponse<AssignmentStatusResponse>.Ok(
            new AssignmentStatusResponse(
                assignment.AssignmentId,
                "AWAITING_ACCEPTANCE",
                nowUtc,
                notes
            ),
            "Job completion submitted for customer acceptance."
        );
    }

    public async Task<ApiResponse<AssignmentCompletionResponse>> AcceptAsync(long assignmentId, AcceptAssignmentRequest? request, CancellationToken ct = default)
    {
        var customerId = RequireCaller(UserRole.Customer, CustomerNotAuthorized);

        var assignment = await workerRepository.GetAssignmentByIdAsync(assignmentId, ct);
        if (assignment == null)
        {
            throw new NotFoundException($"Job assignment with ID {assignmentId} not found.");
        }

        if (assignment.CustomerId != customerId)
        {
            throw new ForbiddenAccessException(CustomerNotAuthorized);
        }

        if (assignment.AssignmentStatus != JobAssignmentStatus.AwaitingAcceptance)
        {
            throw new BusinessRuleViolationException($"Cannot accept assignment in status {assignment.AssignmentStatus}.");
        }

        var nowUtc = clock.UtcNow;
        var (completed, commissionRate, payoutAmount) = await CompleteAsync(assignment, CompletionSource.Customer, nowUtc, ct);
        if (!completed)
        {
            var current = await workerRepository.GetAssignmentByIdAsync(assignmentId, ct)
                ?? throw new NotFoundException($"Job assignment with ID {assignmentId} not found.");
            throw new BusinessRuleViolationException($"Cannot accept assignment in status {current.AssignmentStatus}.");
        }

        return ApiResponse<AssignmentCompletionResponse>.Ok(
            new AssignmentCompletionResponse(
                assignment.AssignmentId,
                "COMPLETED",
                assignment.GrossAmount,
                commissionRate,
                payoutAmount,
                nowUtc
            ),
            "Job assignment completed successfully."
        );
    }

    public async Task<bool> CompleteBySystemAsync(long assignmentId, CompletionSource source, CancellationToken ct = default)
    {
        if (source == CompletionSource.Customer) throw new ArgumentException("A customer accept goes through AcceptAsync.", nameof(source));
        var assignment = await workerRepository.GetAssignmentByIdAsync(assignmentId, ct);
        if (assignment is null || assignment.AssignmentStatus != JobAssignmentStatus.AwaitingAcceptance) return false;
        return (await CompleteAsync(assignment, source, clock.UtcNow, ct)).Completed;
    }

    /// <summary>
    /// AWAITING_ACCEPTANCE -> COMPLETED with the payout and, for the worker, BUSY -> IDLE and CompletedJobs + 1, as one conditional write: of
    /// two completions running at the same time (two accepts, an accept and the timer, the timer and a verdict) only one completes the
    /// assignment and publishes <c>JobCompleted</c> (the rating window, the ledger earning and the order status follow it).
    /// </summary>
    private async Task<(bool Completed, decimal CommissionRate, decimal PayoutAmount)> CompleteAsync(
        Domain.Entities.JobAssignment assignment, CompletionSource source, DateTime nowUtc, CancellationToken ct)
    {
        // Payout computation (Freelancer commission 20% -> 80% payout; Agency per package commission rate)
        decimal commissionRate = assignment.AgencyId == null ? 0.20m : assignment.CommissionRate;
        decimal payoutAmount = Vnd.Net(assignment.GrossAmount, commissionRate);

        var completed = await workerRepository.TryCompleteAssignmentAsync(
            assignment.AssignmentId, assignment.WorkerId, commissionRate, payoutAmount, nowUtc, source, ct);
        if (!completed) return (false, commissionRate, payoutAmount);

        await publisher.Publish(
            new JobCompleted(assignment.AssignmentId, assignment.OrderId, assignment.WorkerId, assignment.AgencyId, payoutAmount, nowUtc), ct);
        return (true, commissionRate, payoutAmount);
    }

    public async Task<ApiResponse<AssignmentStatusResponse>> RequestRedoAsync(long assignmentId, RequestRedoRequest request, CancellationToken ct = default)
    {
        var customerId = RequireCaller(UserRole.Customer, CustomerNotAuthorized);

        if (request == null || string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ValidationException("reason", "Redo reason is required.");
        }

        var assignment = await workerRepository.GetAssignmentByIdAsync(assignmentId, ct);
        if (assignment == null)
        {
            throw new NotFoundException($"Job assignment with ID {assignmentId} not found.");
        }

        if (assignment.CustomerId != customerId)
        {
            throw new ForbiddenAccessException(CustomerNotAuthorized);
        }

        if (assignment.AssignmentStatus != JobAssignmentStatus.AwaitingAcceptance)
        {
            throw new BusinessRuleViolationException($"Cannot request redo for assignment in status {assignment.AssignmentStatus}.");
        }

        var nowUtc = clock.UtcNow;

        // Decision D35: the first "not clean" of a large house opens the 2nd job (same worker, extra hours after the shift, no extra charge)
        // instead of a redo. The redo window of D32 does not apply: the worker keeps working.
        if (continuations is not null && await continuations.TryOpenAsync(assignment.OrderId, nowUtc, ct))
        {
            assignment.TransitionTo(JobAssignmentStatus.InProgress);
            var continuationNote = $"Second job opened: {request.Reason.Trim()}";
            assignment.WorkZone = continuationNote;
            assignment.UpdatedAt = nowUtc;
            assignment.SubmittedAt = null;
            await workerRepository.SaveChangesAsync(ct);

            return ApiResponse<AssignmentStatusResponse>.Ok(
                new AssignmentStatusResponse(assignment.AssignmentId, "IN_PROGRESS", nowUtc, continuationNote),
                "The second job of this large house is open.");
        }

        // Decision D32: a redo is done on site, so only while the worker is still there (RedoMinutes after the submit).
        if (!AcceptanceWindow.CanRedo(assignment.SubmittedAt, nowUtc, _acceptance))
        {
            throw new BusinessRuleViolationException(
                $"Người làm đã rời đi ({_acceptance.RedoMinutes} phút sau khi bàn giao), không thể nhờ dọn lại nữa. Bạn có thể nghiệm thu hoặc gửi khiếu nại.",
                AcceptanceErrorCodes.RedoWindowClosed);
        }

        assignment.TransitionTo(JobAssignmentStatus.InProgress);
        var reworkNote = $"Rework requested: {request.Reason.Trim()}";
        assignment.WorkZone = reworkNote;
        assignment.UpdatedAt = nowUtc;
        assignment.SubmittedAt = null;
        assignment.RedoCount = (byte)Math.Min(byte.MaxValue, assignment.RedoCount + 1);

        await workerRepository.SaveChangesAsync(ct);

        return ApiResponse<AssignmentStatusResponse>.Ok(
            new AssignmentStatusResponse(
                assignment.AssignmentId,
                "IN_PROGRESS",
                nowUtc,
                reworkNote
            ),
            "Rework request submitted to worker."
        );
    }
}
