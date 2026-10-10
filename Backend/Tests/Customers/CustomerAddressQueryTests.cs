using CommonService.Application.Features.Customers;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Modules.Customers;
using Xunit;

namespace CommonService.Tests.Customers;

/// <summary>BE-M2-12: the real ICustomerAddressQuery (contract booking.md B2) over the Customers repository.</summary>
public sealed class CustomerAddressQueryTests
{
    private sealed class Addresses : ICustomerAddressRepository
    {
        public List<CustomerAddress> Items { get; } = [];

        public Task<CustomerAddress?> GetOwnedAsync(int customerId, int addressId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(a => a.AddressId == addressId && a.CustomerId == customerId));

        public Task<IReadOnlyList<CustomerAddress>> ListByCustomerAsync(int customerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsReferencedByOrderAsync(int addressId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CustomerAddress?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CustomerAddress>> ListAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CustomerAddress> AddAsync(CustomerAddress entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Update(CustomerAddress entity) => throw new NotSupportedException();
        public void Delete(CustomerAddress entity) => throw new NotSupportedException();
    }

    private static Addresses WithHouse()
    {
        var addresses = new Addresses();
        addresses.Items.Add(new CustomerAddress
        {
            AddressId = 3,
            CustomerId = 7,
            Label = "Home",
            AddressLine = "1 Test St",
            District = "D1",
            City = "HCMC",
            HousingType = HousingType.House,
            FloorAreaM2 = 45m,
            NumFloors = 2,
            Latitude = 10.76m,
            Longitude = 106.66m,
        });
        return addresses;
    }

    [Fact]
    public async Task GetOwned_ReturnsTheAddress_WithTheServerComputedTotalAreaAndCoordinates()
    {
        var info = await new CustomerAddressQuery(WithHouse()).GetOwnedAsync(7, 3);

        Assert.NotNull(info);
        Assert.Equal(3, info.AddressId);
        Assert.Equal(90m, info.TotalAreaM2); // S_total = 45 x 2 floors
        Assert.Equal(10.76m, info.Latitude);
        Assert.Equal(106.66m, info.Longitude);
    }

    [Fact]
    public async Task GetOwned_IsNull_ForAnotherCustomersAddress_AndForAnUnknownOne()
    {
        var query = new CustomerAddressQuery(WithHouse());

        Assert.Null(await query.GetOwnedAsync(8, 3));
        Assert.Null(await query.GetOwnedAsync(7, 999));
    }
}
