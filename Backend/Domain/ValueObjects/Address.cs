namespace CommonService.Domain.ValueObjects;

public class Address
{
    public string Street { get; private set; }
    public string City { get; private set; }

    private Address(string street, string city)
    {
        Street = street;
        City = city;
    }

    public static Address Create(string street, string city)
        => new Address(street, city);

    public override bool Equals(object? obj)
    {
        if (obj is not Address other) return false;
        return Street == other.Street && City == other.City;
    }
    public override int GetHashCode() => HashCode.Combine(Street, City);
}

