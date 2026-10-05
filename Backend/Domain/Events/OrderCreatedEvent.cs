using CommonService.Domain.Entities;
using MediatR;

namespace CommonService.Domain.Events;

public class OrderCreatedEvent : INotification
{
    public Order Order { get; }

    public OrderCreatedEvent(Order order)
    {
        Order = order;
    }
}
