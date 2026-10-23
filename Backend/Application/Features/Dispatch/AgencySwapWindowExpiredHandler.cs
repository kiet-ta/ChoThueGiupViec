using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Application.Features.Dispatch;

/// <summary>
/// Listens to <see cref="AgencySwapWindowExpired"/> (published by Agencies M5 when the 30-minute self-swap window ends,
/// or when notice is 2 hours or less) and triggers the Emergency Rescue flow in <see cref="MilestoneFailoverService"/>.
/// </summary>
public sealed class AgencySwapWindowExpiredHandler : INotificationHandler<AgencySwapWindowExpired>
{
    private readonly MilestoneFailoverService _failoverService;
    private readonly IMilestoneFailoverRepository _repository;
    private readonly ILogger<AgencySwapWindowExpiredHandler> _logger;

    public AgencySwapWindowExpiredHandler(
        MilestoneFailoverService failoverService,
        IMilestoneFailoverRepository repository,
        ILogger<AgencySwapWindowExpiredHandler>? logger = null)
    {
        _failoverService = failoverService ?? throw new ArgumentNullException(nameof(failoverService));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? NullLogger<AgencySwapWindowExpiredHandler>.Instance;
    }

    public async Task Handle(AgencySwapWindowExpired notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Received AgencySwapWindowExpired for AssignmentId={AssignmentId}, OrderId={OrderId}, AgencyId={AgencyId}. Reason: {Reason}",
            notification.AssignmentId, notification.OrderId, notification.AgencyId, notification.Reason);

        try
        {
            // Fetch the original assignment, order, and slot from the repository
            var assignment = await _repository.GetAssignmentAsync(notification.AssignmentId, cancellationToken);
            if (assignment == null)
            {
                _logger.LogWarning("Assignment {AssignmentId} not found; cannot trigger emergency rescue.", notification.AssignmentId);
                return;
            }

            var order = await _repository.GetOrderAsync(notification.OrderId, cancellationToken);
            var slot = await _repository.GetSlotAsync(assignment.SlotId, cancellationToken);

            // Trigger the emergency rescue
            var result = await _failoverService.TriggerEmergencyRescueAsync(assignment, order, slot, notification.Reason, cancellationToken);

            _logger.LogInformation(
                "Emergency rescue for AssignmentId={AssignmentId} completed with status {Status}: {Message}",
                notification.AssignmentId, result.Status, result.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Emergency rescue for AssignmentId={AssignmentId} failed.", notification.AssignmentId);
            // Do not rethrow - the event is already processed, we log the error but don't fail the handler
        }
    }
}