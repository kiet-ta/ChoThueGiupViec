using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CommonService.Application.Features.Workers.Services;

/// <summary>
/// Decision D32: a verdict closes a job that is still waiting for the customer, whatever the outcome (resolved or dismissed). The event is
/// published once per assignment of the order, the cancelled offers included, so only an AWAITING_ACCEPTANCE row is completed
/// (<see cref="IJobCompletionService.CompleteBySystemAsync"/> ignores the others). The refund and the penalty were already applied by the
/// verdict; completing books the earning through <c>JobCompleted</c>, so a compensation shows as earning minus penalty. Never throws: the
/// verdict is committed, a failure is logged.
/// </summary>
public class DisputeResolvedAcceptanceHandler(IJobCompletionService completion, ILogger<DisputeResolvedAcceptanceHandler> logger)
    : INotificationHandler<DisputeResolved>
{
    public async Task Handle(DisputeResolved notification, CancellationToken cancellationToken)
    {
        try
        {
            if (await completion.CompleteBySystemAsync(notification.AssignmentId, CompletionSource.Verdict, cancellationToken))
            {
                logger.LogInformation("Dispute {DisputeId}: assignment {AssignmentId} completed by the verdict.", notification.DisputeId, notification.AssignmentId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Dispute {DisputeId}: could not complete assignment {AssignmentId} after the verdict.", notification.DisputeId, notification.AssignmentId);
        }
    }
}
