using CommonService.Domain.Events;
using MediatR;

namespace CommonService.Tests.Dispatch;

public sealed class DispatchOrderPaidTestHandler : INotificationHandler<OrderPaid>
{
    public static readonly List<OrderPaid> Received = [];

    public Task Handle(OrderPaid notification, CancellationToken cancellationToken)
    {
        Received.Add(notification);
        return Task.CompletedTask;
    }
}
