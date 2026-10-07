using CommonService.Domain.Events;
using MediatR;

namespace CommonService.Application.Features.Admin.Services;

/// <summary>
/// Q12: when a customer rating arrives, a Super-Freelancer whose rating average fell below 4.70 loses the flag.
/// Only CUSTOMER ratings move a worker's average; the event carries no worker id, so it is read from the assignment.
/// </summary>
public sealed class RatingSubmittedSuperFreelancerHandler(ISuperFreelancerRepository workers, ISuperFreelancerService superFreelancers)
    : INotificationHandler<RatingSubmitted>
{
    public async Task Handle(RatingSubmitted notification, CancellationToken cancellationToken)
    {
        if (!string.Equals(notification.RaterRole, "CUSTOMER", StringComparison.Ordinal)) return;

        var workerId = await workers.GetWorkerIdOfAssignmentAsync(notification.AssignmentId, cancellationToken);
        if (workerId is int id) await superFreelancers.AutoRevokeIfBelowThresholdAsync(id, cancellationToken);
    }
}
