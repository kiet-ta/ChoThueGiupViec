using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using MediatR;

namespace CommonService.Application.Features.Workers.Commands;

public sealed record SubmitJobCompletionCommand(
    long AssignmentId,
    string? Notes = null
) : IRequest<ApiResponse<AssignmentStatusResponse>>;

public sealed class SubmitJobCompletionCommandHandler(
    IWorkerRepository workerRepository,
    ICurrentUser currentUser) : IRequestHandler<SubmitJobCompletionCommand, ApiResponse<AssignmentStatusResponse>>
{
    public async Task<ApiResponse<AssignmentStatusResponse>> Handle(SubmitJobCompletionCommand command, CancellationToken ct)
    {
        var assignment = await workerRepository.GetAssignmentByIdAsync(command.AssignmentId, ct);
        if (assignment == null)
        {
            throw new NotFoundException($"Job assignment with ID {command.AssignmentId} not found.");
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
        var beforePhotos = await workerRepository.GetAcceptedJobPhotosByPhaseAsync(command.AssignmentId, "BEFORE", ct);
        var afterPhotos = await workerRepository.GetAcceptedJobPhotosByPhaseAsync(command.AssignmentId, "AFTER", ct);

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
                command.Notes
            ),
            "Job completion submitted for customer acceptance."
        );
    }
}
