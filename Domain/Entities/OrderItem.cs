using CommonService.Domain.ValueObjects;

namespace CommonService.Domain.Entities;
public class OrderItem
{
    public Guid ProductId { get; private set; }
    public Money UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public Money Subtotal => UnitPrice.Multiply(Quantity);

    internal OrderItem(Guid productId, Money unitPrice, int quantity)
    {
        ProductId = productId;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }
}
