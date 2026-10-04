using CommonService.Domain.Entities;
using CommonService.Domain.ValueObjects;

namespace CommonService.Domain.Services;

public class ShippingCalculator
{
    public Money CalculateShipping(Order order, Address destination)
    {
        var baseCost = 5m;
        var extra = order.Items.Count > 3 ? 10m : 0m;

        return new Money(baseCost + extra, "USD");
    }
}

