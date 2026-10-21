using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CommonService.Application.Features.Booking;

/// <summary>
/// Booking's answer to <see cref="JobCompleted"/> (contract booking.md section 5, decision Q22 D7): the order follows its assignments.
/// An ASSIGNED order whose required workers have all COMPLETED becomes COMPLETED (<c>JobOrder.SyncWithAssignments</c>); with two workers it
/// stays ASSIGNED until the second one completes. Idempotent: a repeated event, an unknown order and an order in any other status change nothing.
/// The event is published inside the customer's accept call, so this handler only acts on an ASSIGNED order and never throws for a state it does not handle.
/// </summary>
public sealed class JobCompletedHandler(
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<JobCompletedHandler> logger) : INotificationHandler<JobCompleted>
{
    public async Task Handle(JobCompleted notification, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var order = await orders.GetForUpdateAsync(notification.OrderId, cancellationToken);
            if (order is null)
            {
                logger.LogWarning("JobCompleted for unknown order {OrderId} ignored.", notification.OrderId);
                return false;
            }

            if (order.OrderStatus != JobOrderStatus.Assigned)
            {
                return false;
            }

            var statuses = await orders.GetAssignmentStatusesAsync(notification.OrderId, cancellationToken);
            if (!order.SyncWithAssignments(statuses))
            {
                return false;
            }

            order.UpdatedAt = now;
            return true;
        }, cancellationToken);
    }
}
