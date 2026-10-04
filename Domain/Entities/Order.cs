using CommonService.Domain.ValueObjects;

namespace CommonService.Domain.Entities;
public class Order // Aggregate Root
{
    private readonly List<OrderItem> _items = new();

    public Guid Id { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();
    public Money TotalAmount => new Money(_items.Sum(i => i.Subtotal.Amount), "USD");

    private Order() { } // For EF Core

    public Order(Guid id)
    {
        Id = id;
        CreatedAt = DateTime.UtcNow;
    }

    //public void AddItem(Money product, int quantity)
    //{
    //    if (quantity <= 0) throw new ArgumentException("Quantity must be > 0");

    //    var item = new OrderItem(product.Id, product.Price, quantity);
    //    _items.Add(item);
    //}
}


