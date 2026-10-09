using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using MediatR;

namespace CommonService.Application.Features.Workers.Commands;

public sealed record RequestJobRedoCommand(
    long AssignmentId,
    RequestRedoRequest Request
) : IRequest<ApiResponse<AssignmentStatusResponse>>;

public sealed class RequestJobRedoCommandHandler(
    IWorkerRepository workerRepository,
    ICurrentUser currentUser) : IRequestHandler<RequestJobRedoCommand, ApiResponse<AssignmentStatusResponse>>
{
    public async Task<ApiResponse<AssignmentStatusResponse>> Handle(RequestJobRedoCommand command, CancellationToken ct)
    {
        if (command.Request == null || string.IsNullOrWhiteSpace(command.Request.Reason))
        {
            throw new ValidationException("reason", "Redo reason is required.");
        }

        var assignment = await workerRepository.GetAssignmentByIdAsync(command.AssignmentId, ct);
        if (assignment == null)
        {
            throw new NotFoundException($"Job assignment with ID {command.AssignmentId} not found.");
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
        var reworkNote = $"Rework requested: {command.Request.Reason.Trim()}";
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
