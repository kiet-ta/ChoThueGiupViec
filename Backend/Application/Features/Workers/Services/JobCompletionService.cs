using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Domain.ValueObjects;
using MediatR;

namespace CommonService.Application.Features.Workers.Services;

public sealed class JobCompletionService(
    IWorkerRepository workerRepository,
    IPublisher publisher,
    ICurrentUser currentUser) : IJobCompletionService
{
    public async Task<ApiResponse<AssignmentStatusResponse>> SubmitAsync(long assignmentId, string? notes, CancellationToken ct = default)
    {
        var assignment = await workerRepository.GetAssignmentByIdAsync(assignmentId, ct);
        if (assignment == null)
        {
            throw new NotFoundException($"Job assignment with ID {assignmentId} not found.");
        }

        if (currentUser.IsAuthenticated && currentUser.Role == UserRole.Worker && currentUser.UserId is int workerId && workerId != 0 && assignment.WorkerId != workerId)
        {
            throw new ForbiddenAccessException("Worker is not assigned to this job assignment.");
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

        assignment.TransitionTo(JobAssignmentStatus.AwaitingAcceptance);
        assignment.UpdatedAt = DateTime.UtcNow;

        await workerRepository.SaveChangesAsync(ct);

        return ApiResponse<AssignmentStatusResponse>.Ok(
            new AssignmentStatusResponse(
                assignment.AssignmentId,
                "AWAITING_ACCEPTANCE",
                DateTime.UtcNow,
                notes
            ),
            "Job completion submitted for customer acceptance."
        );
    }

    public async Task<ApiResponse<AssignmentCompletionResponse>> AcceptAsync(long assignmentId, AcceptAssignmentRequest? request, CancellationToken ct = default)
    {
        var assignment = await workerRepository.GetAssignmentByIdAsync(assignmentId, ct);
        if (assignment == null)
        {
            throw new NotFoundException($"Job assignment with ID {assignmentId} not found.");
        }

        if (currentUser.IsAuthenticated && currentUser.Role == UserRole.Customer && currentUser.UserId is int customerId && customerId != 0 && assignment.CustomerId != customerId)
        {
            throw new ForbiddenAccessException("Customer is not authorized for this assignment.");
        }

        if (assignment.AssignmentStatus != JobAssignmentStatus.AwaitingAcceptance)
        {
            throw new BusinessRuleViolationException($"Cannot accept assignment in status {assignment.AssignmentStatus}.");
        }

        var nowUtc = DateTime.UtcNow;

        // Move assignment to COMPLETED state
        assignment.TransitionTo(JobAssignmentStatus.Completed);
        assignment.CompletedAt = nowUtc;
        assignment.CustomerConfirmedAt = nowUtc;

        // Payout computation (Freelancer commission 20% -> 80% payout; Agency per package commission rate)
        decimal commissionRate = assignment.AgencyId == null ? 0.20m : assignment.CommissionRate;
        assignment.CommissionRate = commissionRate;
        decimal payoutAmount = Vnd.Net(assignment.GrossAmount, commissionRate);
        assignment.PayoutAmount = payoutAmount;
        assignment.UpdatedAt = nowUtc;

        // If worker is Freelancer / busy staff, transition WorkStatus back to IDLE & increment completed jobs count
        var worker = await workerRepository.GetByIdAsync(assignment.WorkerId, ct);
        if (worker != null)
        {
            if (worker.WorkStatus == WorkStatus.Busy)
            {
                worker.TransitionTo(WorkStatus.Idle);
            }
            worker.CompletedJobs += 1;
            worker.UpdatedAt = nowUtc;
            workerRepository.Update(worker);
        }

        await workerRepository.SaveChangesAsync(ct);

        // Emit JobCompleted domain event (M6 rating window opens, payout batch accumulator receives item)
        await publisher.Publish(
            new JobCompleted(
                assignment.AssignmentId,
                assignment.OrderId,
                assignment.WorkerId,
                assignment.AgencyId,
                payoutAmount,
                nowUtc
            ),
            ct
        );

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

    public async Task<ApiResponse<AssignmentStatusResponse>> RequestRedoAsync(long assignmentId, RequestRedoRequest request, CancellationToken ct = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ValidationException("reason", "Redo reason is required.");
        }

        var assignment = await workerRepository.GetAssignmentByIdAsync(assignmentId, ct);
        if (assignment == null)
        {
            throw new NotFoundException($"Job assignment with ID {assignmentId} not found.");
        }

        if (currentUser.IsAuthenticated && currentUser.Role == UserRole.Customer && currentUser.UserId is int customerId && customerId != 0 && assignment.CustomerId != customerId)
        {
            throw new ForbiddenAccessException("Customer is not authorized for this assignment.");
        }

        if (assignment.AssignmentStatus != JobAssignmentStatus.AwaitingAcceptance)
        {
            throw new BusinessRuleViolationException($"Cannot request redo for assignment in status {assignment.AssignmentStatus}.");
        }

        assignment.TransitionTo(JobAssignmentStatus.InProgress);
        var reworkNote = $"Rework requested: {request.Reason.Trim()}";
        assignment.WorkZone = reworkNote;
        assignment.UpdatedAt = DateTime.UtcNow;

        await workerRepository.SaveChangesAsync(ct);

        return ApiResponse<AssignmentStatusResponse>.Ok(
            new AssignmentStatusResponse(
                assignment.AssignmentId,
                "IN_PROGRESS",
                DateTime.UtcNow,
                reworkNote
            ),
            "Rework request submitted to worker."
        );
    }
}
