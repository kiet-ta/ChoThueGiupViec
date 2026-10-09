using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Domain.ValueObjects;
using MediatR;

namespace CommonService.Application.Features.Workers.Commands;

public sealed record AcceptJobCompletionCommand(
    long AssignmentId,
    AcceptAssignmentRequest? Request = null
) : IRequest<ApiResponse<AssignmentCompletionResponse>>;

public sealed class AcceptJobCompletionCommandHandler(
    IWorkerRepository workerRepository,
    IPublisher publisher,
    ICurrentUser currentUser) : IRequestHandler<AcceptJobCompletionCommand, ApiResponse<AssignmentCompletionResponse>>
{
    public async Task<ApiResponse<AssignmentCompletionResponse>> Handle(AcceptJobCompletionCommand command, CancellationToken ct)
    {
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
}
