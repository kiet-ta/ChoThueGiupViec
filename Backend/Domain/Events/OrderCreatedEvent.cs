using CommonService.Domain.Entities;

namespace CommonService.Domain.Events;

public class OrderCreatedEvent
{
    public Order Order { get; }

    public OrderCreatedEvent(Order order)
    {
        Order = order;
    }
}
