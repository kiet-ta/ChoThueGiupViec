using CommonService.Domain.Events;
using MediatR;

namespace CommonService.Tests.Booking;

public sealed class BookingOrderPaidTestHandler : INotificationHandler<OrderPaid>
{
    public static readonly List<OrderPaid> Received = [];

    public Task Handle(OrderPaid notification, CancellationToken cancellationToken)
    {
        Received.Add(notification);
        return Task.CompletedTask;
    }
}
