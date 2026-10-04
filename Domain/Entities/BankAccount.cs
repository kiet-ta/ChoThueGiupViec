using CommonService.Domain.ValueObjects;

namespace CommonService.Domain.Entities;

public class BankAccount
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Email Email { get; private set; }
    public Money Balance { get; private set; }

    public BankAccount(Email email)
    {
        Email = email;
        Balance = new Money(0, "USD");
    }

    public void Deposit(Money money)
    {
        Balance = Balance.Add(money);
    }

}

