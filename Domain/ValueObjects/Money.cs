// Note: Money là Value Object vì:
//Không cần ID.
//Chỉ quan tâm Amount và Currency.
//Hai Money(100, "USD") giống nhau → coi là cùng một đối tượng.

namespace CommonService.Domain.ValueObjects;
public class Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        if (amount < 0) throw new ArgumentException("Invalid amount");
        if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("Missing currency");

        Amount = amount;
        Currency = currency;
    }

    // Factory Method
    public static Money Create(decimal amount, string currency)
        => new Money(amount, currency);

    public Money Add(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException("Khác đơn vị tiền tệ");
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Multiply(int quantity)
    {
        return new Money(Amount * quantity, Currency);
    }

    // Override Equals and GetHashCode to compare by value
    public override bool Equals(object? obj)
    {
        if (obj is not Money other) return false;
        return Amount == other.Amount && Currency == other.Currency;
    }

    public override int GetHashCode() => HashCode.Combine(Amount, Currency);

    // Sum
    public static Money operator + (Money a, Money b)
    {
        if (a.Currency != b.Currency)
            throw new InvalidOperationException("Currency must match");
        return Create(a.Amount + b.Amount, a.Currency);
    }
}
