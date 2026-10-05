using System.Reflection;
using CommonService.Application.Common.Models;
using CommonService.Application.Common.Options;
using CommonService.Application.Features.Customers.Dtos;
using CommonService.Application.Features.Customers.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Modules.Customers;
using CommonService.Infrastructure.Persistence;
using CommonService.WebAPI.Controllers.Customers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CommonService.Tests.Customers;

/// <summary>
/// BE-M1-05: address book (contract customers.md §2.2, decisions Q21 C4/C5, G-2, G-4).
/// DB-backed tests run against the local SQL Server of BASE-07 and delete the rows they create.
/// </summary>
public class CustomerAddressTests
{
    private const string ConnectionString =
        "Server=localhost;Database=ChoThueGiupViec;Integrated Security=True;TrustServerCertificate=True;";

    private static bool IsSqlServerAvailable()
    {
        try
        {
            using var conn = new SqlConnection(ConnectionString + "Connect Timeout=3;");
            conn.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);

    private sealed class TestClock(DateTime initialUtc) : IClock
    {
        public DateTime UtcNow { get; set; } = initialUtc;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => local.AddHours(-7);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(UtcNow));
    }

    /// <summary>One service graph on a real context, plus the customers a test created (removed with their rows on dispose).</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly List<int> _customerIds = [];

        public AppDbContext Db { get; } = CreateContext();
        public TestClock Clock { get; } = new(new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc));
        public CustomerAddressService Service { get; }

        public Fixture()
        {
            Service = new CustomerAddressService(
                new CustomerRepository(Db),
                new CustomerAddressRepository(Db),
                new UnitOfWork(Db),
                Clock,
                Microsoft.Extensions.Options.Options.Create(new BusinessRules()));
        }

        public async Task<int> AddCustomerAsync()
        {
            var created = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
            var customer = new Customer
            {
                PhoneNumber = $"09{Random.Shared.Next(10000000, 99999999)}",
                FullName = "Address Test",
                OtpVerifiedAt = created,
                TrustScore = 0.00m,
                AccountStatus = CustomerAccountStatus.Active,
                CreatedAt = created,
                UpdatedAt = created
            };
            Db.Customers.Add(customer);
            await Db.SaveChangesAsync();
            _customerIds.Add(customer.CustomerId);
            return customer.CustomerId;
        }

        /// <summary>Creates an address and advances the clock so CreatedAt differs between addresses.</summary>
        public async Task<AddressDto> CreateAsync(int customerId, AddressRequestDto? request = null)
        {
            var result = await Service.CreateAsync(customerId, request ?? Request());
            Assert.True(result.Success, $"create failed: {result.ErrorMessage}");
            Clock.UtcNow = Clock.UtcNow.AddMinutes(1);
            return result.Data!;
        }

        public async Task<long> AddOrderAsync(int customerId, int addressId)
        {
            var order = new JobOrder
            {
                OrderCode = $"T{Guid.NewGuid():N}"[..20],
                CustomerId = customerId,
                AddressId = addressId,
                ServiceTier = ServiceTier.Economy,
                ScheduledDate = new DateOnly(2026, 10, 20),
                ShiftCode = "MORNING",
                AreaSnapshotM2 = 60m,
                RequiredWorkers = 1,
                TotalAmount = 260000m,
                CreatedAt = Clock.UtcNow,
                UpdatedAt = Clock.UtcNow
            };
            Db.JobOrders.Add(order);
            await Db.SaveChangesAsync();

            // A request never tracks an order it did not load; keep the shared context from doing so either.
            Db.Entry(order).State = EntityState.Detached;
            return order.OrderId;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var id in _customerIds)
            {
                await Db.JobOrders.Where(o => o.CustomerId == id).ExecuteDeleteAsync();
                await Db.CustomerAddresses.Where(a => a.CustomerId == id).ExecuteDeleteAsync();
                await Db.Customers.Where(c => c.CustomerId == id).ExecuteDeleteAsync();
            }

            await Db.DisposeAsync();
        }
    }

    private static AddressRequestDto Request(
        string housingType = "APARTMENT",
        decimal? floorArea = 60m,
        int? floors = 1,
        bool isDefault = false,
        string label = "Home") => new()
        {
            Label = label,
            AddressLine = "12 Nguyen Hue",
            District = "District 1",
            City = "Ho Chi Minh City",
            HousingType = housingType,
            FloorAreaM2 = floorArea,
            NumFloors = floors,
            Bedrooms = 2,
            Bathrooms = 1,
            Latitude = 10.776889m,
            Longitude = 106.700806m,
            IsDefault = isDefault
        };

    private static async Task<List<CustomerAddress>> StoredAsync(int customerId)
    {
        await using var db = CreateContext();
        return await db.CustomerAddresses.AsNoTracking().Where(a => a.CustomerId == customerId).ToListAsync();
    }

    // ---- S_total computed by the server (PRD §1.1) ----

    [Fact]
    public async Task Create_computes_S_total_as_floor_area_times_floors_on_the_server()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();

        var result = await fx.Service.CreateAsync(customerId, Request("HOUSE", 45.5m, 3));

        Assert.True(result.Success);
        Assert.Equal(201, result.StatusCode);
        Assert.Equal(136.50m, result.Data!.TotalAreaM2);

        var stored = Assert.Single(await StoredAsync(customerId));
        Assert.Equal(136.50m, stored.TotalAreaM2); // the DB computed column agrees with the server value
    }

    [Fact]
    public async Task S_total_is_rounded_to_2_decimals_half_away_from_zero()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();

        // 33.335 -> 33.34 (stored area), x 3 floors = 100.02
        var result = await fx.Service.CreateAsync(customerId, Request("HOUSE", 33.335m, 3));

        Assert.True(result.Success);
        Assert.Equal(33.34m, result.Data!.FloorAreaM2);
        Assert.Equal(100.02m, result.Data.TotalAreaM2);
    }

    [Fact]
    public void ComputeTotalArea_rounds_half_away_from_zero()
    {
        Assert.Equal(0.03m, CustomerAddressService.ComputeTotalArea(0.015m, 2));
        Assert.Equal(60.00m, CustomerAddressService.ComputeTotalArea(60m, 1));
        Assert.Equal(99999.90m, CustomerAddressService.ComputeTotalArea(9999.99m, 10));
    }

    [Fact]
    public void AddressRequestDto_has_no_totalAreaM2_so_a_client_value_is_ignored()
    {
        var names = typeof(AddressRequestDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name);

        Assert.DoesNotContain("TotalAreaM2", names);
    }

    [Fact]
    public async Task Update_recomputes_S_total_and_keeps_created_at()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();
        var created = await fx.CreateAsync(customerId, Request("HOUSE", 50m, 2));

        var result = await fx.Service.UpdateAsync(customerId, created.AddressId, Request("HOUSE", 40m, 4, label: "Villa"));

        Assert.True(result.Success);
        Assert.Equal(160.00m, result.Data!.TotalAreaM2);
        Assert.Equal("Villa", result.Data.Label);
        Assert.Equal(created.CreatedAt, result.Data.CreatedAt);
        Assert.Equal(160.00m, (await StoredAsync(customerId)).Single().TotalAreaM2);
    }

    // ---- validation: boundaries that are valid (need the database to insert) ----

    [Theory]
    [InlineData("ROOM", 30.00, 1)]
    [InlineData("ROOM", 0.01, 1)]
    [InlineData("HOUSE", 120.00, 10)]
    [InlineData("HOUSE", 120.00, 1)]
    [InlineData("APARTMENT", 9999.99, 1)]
    [InlineData("house", 80.00, 2)]
    public async Task Create_accepts_values_on_the_valid_boundaries(string housing, double area, int floors)
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();

        var result = await fx.Service.CreateAsync(customerId, Request(housing, (decimal)area, floors));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(housing.ToUpperInvariant(), result.Data!.HousingType);
    }

    [Fact]
    public async Task Create_accepts_coordinate_extremes_and_null_bedrooms_and_bathrooms()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();
        var request = Request();
        request.Latitude = -90m;
        request.Longitude = 180m;
        request.Bedrooms = null;
        request.Bathrooms = 0;

        var result = await fx.Service.CreateAsync(customerId, request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Null(result.Data!.Bedrooms);
        Assert.Equal(0, result.Data.Bathrooms);
    }

    [Fact]
    public async Task Create_accepts_text_fields_of_exactly_the_maximum_length()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();
        var request = Request();
        request.Label = new string('l', 50);
        request.AddressLine = new string('a', 255);
        request.District = new string('d', 100);
        request.City = new string('c', 100);

        var result = await fx.Service.CreateAsync(customerId, request);

        Assert.True(result.Success, result.ErrorMessage);
    }

    // ---- validation: rejected values (validation runs before any query) ----

    private static async Task AssertRejectedAsync(string field, AddressRequestDto request)
    {
        await using var fx = new Fixture();

        var result = await fx.Service.CreateAsync(1, request);

        Assert.False(result.Success);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains(field, result.ValidationErrors!.Keys);
    }

    [Theory]
    [InlineData("ROOM", 30.01, 1)]
    [InlineData("ROOM", 100.0, 1)]
    public async Task Room_area_above_the_configured_maximum_is_rejected(string housing, double area, int floors) =>
        await AssertRejectedAsync("floorAreaM2", Request(housing, (decimal)area, floors));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10000)]
    [InlineData(9999.995)]
    public async Task Floor_area_outside_0_to_9999_99_is_rejected(double area) =>
        await AssertRejectedAsync("floorAreaM2", Request("APARTMENT", (decimal)area, 1));

    [Fact]
    public async Task Floor_area_that_rounds_to_zero_is_rejected() =>
        await AssertRejectedAsync("floorAreaM2", Request("APARTMENT", 0.004m, 1));

    [Fact]
    public async Task Missing_floor_area_is_rejected() =>
        await AssertRejectedAsync("floorAreaM2", Request(floorArea: null));

    [Theory]
    [InlineData("APARTMENT", 2)]
    [InlineData("ROOM", 2)]
    [InlineData("HOUSE", 11)]
    [InlineData("HOUSE", 0)]
    [InlineData("HOUSE", 256)]
    [InlineData("APARTMENT", -1)]
    public async Task Number_of_floors_outside_the_rule_of_the_housing_type_is_rejected(string housing, int floors) =>
        await AssertRejectedAsync("numFloors", Request(housing, 20m, floors));

    [Fact]
    public async Task Missing_number_of_floors_is_rejected() =>
        await AssertRejectedAsync("numFloors", Request(floors: null));

    [Theory]
    [InlineData("VILLA")]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("APARTMENTS")]
    public async Task Unknown_housing_type_is_rejected(string housing) =>
        await AssertRejectedAsync("housingType", Request(housing));

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public async Task Bedrooms_and_bathrooms_outside_0_to_255_are_rejected(int count)
    {
        var bedrooms = Request();
        bedrooms.Bedrooms = count;
        await AssertRejectedAsync("bedrooms", bedrooms);

        var bathrooms = Request();
        bathrooms.Bathrooms = count;
        await AssertRejectedAsync("bathrooms", bathrooms);
    }

    [Theory]
    [InlineData(90.01)]
    [InlineData(-90.01)]
    public async Task Latitude_outside_minus_90_to_90_is_rejected(double latitude)
    {
        var request = Request();
        request.Latitude = (decimal)latitude;
        await AssertRejectedAsync("latitude", request);
    }

    [Theory]
    [InlineData(180.01)]
    [InlineData(-180.01)]
    public async Task Longitude_outside_minus_180_to_180_is_rejected(double longitude)
    {
        var request = Request();
        request.Longitude = (decimal)longitude;
        await AssertRejectedAsync("longitude", request);
    }

    [Fact]
    public async Task Missing_coordinates_are_rejected()
    {
        var noLat = Request();
        noLat.Latitude = null;
        await AssertRejectedAsync("latitude", noLat);

        var noLon = Request();
        noLon.Longitude = null;
        await AssertRejectedAsync("longitude", noLon);
    }

    [Theory]
    [InlineData("label", 0)]
    [InlineData("label", 51)]
    [InlineData("addressLine", 0)]
    [InlineData("addressLine", 256)]
    [InlineData("district", 0)]
    [InlineData("district", 101)]
    [InlineData("city", 0)]
    [InlineData("city", 101)]
    public async Task Text_fields_that_are_empty_or_too_long_are_rejected(string field, int length)
    {
        var request = Request();
        var value = new string('x', length);
        switch (field)
        {
            case "label": request.Label = value; break;
            case "addressLine": request.AddressLine = value; break;
            case "district": request.District = value; break;
            default: request.City = value; break;
        }

        await AssertRejectedAsync(field, request);
    }

    [Fact]
    public async Task Whitespace_only_text_is_rejected()
    {
        var request = Request();
        request.Label = "   ";
        await AssertRejectedAsync("label", request);
    }

    [Fact]
    public async Task Every_invalid_field_is_reported_in_one_response()
    {
        await using var fx = new Fixture();

        var result = await fx.Service.CreateAsync(1, new AddressRequestDto());

        Assert.Equal(400, result.StatusCode);
        foreach (var field in new[] { "label", "addressLine", "district", "city", "housingType", "floorAreaM2", "numFloors", "latitude", "longitude" })
        {
            Assert.Contains(field, result.ValidationErrors!.Keys);
        }
    }

    [Fact]
    public async Task Invalid_create_and_update_write_nothing()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();
        var created = await fx.CreateAsync(customerId);

        var badCreate = await fx.Service.CreateAsync(customerId, Request("ROOM", 31m, 1));
        var badUpdate = await fx.Service.UpdateAsync(customerId, created.AddressId, Request("ROOM", 31m, 1, label: "Changed"));

        Assert.Equal(400, badCreate.StatusCode);
        Assert.Equal(400, badUpdate.StatusCode);
        var stored = Assert.Single(await StoredAsync(customerId));
        Assert.Equal("Home", stored.Label);
        Assert.Equal(HousingType.Apartment, stored.HousingType);
    }

    // ---- default address ----

    [Fact]
    public async Task First_address_becomes_default_even_when_isDefault_is_false()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();

        var first = await fx.CreateAsync(customerId, Request(isDefault: false));

        Assert.True(first.IsDefault);
    }

    [Fact]
    public async Task Later_address_is_not_default_unless_requested()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();
        var first = await fx.CreateAsync(customerId);

        var second = await fx.CreateAsync(customerId, Request(label: "Office"));

        Assert.False(second.IsDefault);
        var stored = await StoredAsync(customerId);
        Assert.Equal(first.AddressId, Assert.Single(stored, a => a.IsDefault).AddressId);
    }

    [Fact]
    public async Task Creating_with_isDefault_true_moves_the_default_and_leaves_exactly_one()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();
        await fx.CreateAsync(customerId);

        var second = await fx.CreateAsync(customerId, Request(label: "Office", isDefault: true));

        Assert.True(second.IsDefault);
        var stored = await StoredAsync(customerId);
        Assert.Equal(2, stored.Count);
        Assert.Equal(second.AddressId, Assert.Single(stored, a => a.IsDefault).AddressId);
    }

    [Fact]
    public async Task Updating_with_isDefault_true_moves_the_default_and_leaves_exactly_one()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();
        await fx.CreateAsync(customerId);
        var second = await fx.CreateAsync(customerId, Request(label: "Office"));

        var result = await fx.Service.UpdateAsync(customerId, second.AddressId, Request(label: "Office", isDefault: true));

        Assert.True(result.Data!.IsDefault);
        var stored = await StoredAsync(customerId);
        Assert.Equal(second.AddressId, Assert.Single(stored, a => a.IsDefault).AddressId);
    }

    [Fact]
    public async Task Updating_the_default_with_isDefault_false_keeps_it_as_default()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();
        var first = await fx.CreateAsync(customerId);

        var result = await fx.Service.UpdateAsync(customerId, first.AddressId, Request(isDefault: false));

        Assert.True(result.Data!.IsDefault);
    }

    [Fact]
    public async Task Default_flags_of_two_customers_are_independent()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var alice = await fx.AddCustomerAsync();
        var bob = await fx.AddCustomerAsync();

        await fx.CreateAsync(alice);
        await fx.CreateAsync(bob);

        Assert.Single(await StoredAsync(alice), a => a.IsDefault);
        Assert.Single(await StoredAsync(bob), a => a.IsDefault);
    }

    // ---- list order ----

    [Fact]
    public async Task List_returns_default_first_then_newest_first_and_empty_is_a_valid_200()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();

        var empty = await fx.Service.ListAsync(customerId);
        Assert.True(empty.Success);
        Assert.Equal(200, empty.StatusCode);
        Assert.Empty(empty.Data!);

        var a = await fx.CreateAsync(customerId, Request(label: "A"));
        var b = await fx.CreateAsync(customerId, Request(label: "B"));
        var c = await fx.CreateAsync(customerId, Request(label: "C"));

        var list = (await fx.Service.ListAsync(customerId)).Data!;
        Assert.Equal(new[] { a.AddressId, c.AddressId, b.AddressId }, list.Select(x => x.AddressId).ToArray());

        await fx.Service.UpdateAsync(customerId, b.AddressId, Request(label: "B", isDefault: true));

        var reordered = (await fx.Service.ListAsync(customerId)).Data!;
        Assert.Equal(new[] { b.AddressId, c.AddressId, a.AddressId }, reordered.Select(x => x.AddressId).ToArray());
    }

    [Fact]
    public async Task List_contains_only_the_addresses_of_the_caller()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var alice = await fx.AddCustomerAsync();
        var bob = await fx.AddCustomerAsync();
        var aliceAddress = await fx.CreateAsync(alice);
        await fx.CreateAsync(bob);

        var list = (await fx.Service.ListAsync(alice)).Data!;

        Assert.Equal(aliceAddress.AddressId, Assert.Single(list).AddressId);
    }

    // ---- ownership: 404, no existence leak ----

    [Fact]
    public async Task Another_customers_address_is_404_on_get_put_and_delete_and_stays_untouched()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var owner = await fx.AddCustomerAsync();
        var intruder = await fx.AddCustomerAsync();
        var address = await fx.CreateAsync(owner);

        var get = await fx.Service.GetAsync(intruder, address.AddressId);
        var put = await fx.Service.UpdateAsync(intruder, address.AddressId, Request(label: "Hacked"));
        var delete = await fx.Service.DeleteAsync(intruder, address.AddressId);

        Assert.Equal(404, get.StatusCode);
        Assert.Equal(404, put.StatusCode);
        Assert.Equal(404, delete.StatusCode);
        var stored = Assert.Single(await StoredAsync(owner));
        Assert.Equal("Home", stored.Label);
    }

    [Fact]
    public async Task Unknown_address_id_is_404_on_get_put_and_delete()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();

        Assert.Equal(404, (await fx.Service.GetAsync(customerId, int.MaxValue)).StatusCode);
        Assert.Equal(404, (await fx.Service.UpdateAsync(customerId, int.MaxValue, Request())).StatusCode);
        Assert.Equal(404, (await fx.Service.DeleteAsync(customerId, int.MaxValue)).StatusCode);
    }

    [Fact]
    public async Task Get_returns_the_owners_address()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();
        var created = await fx.CreateAsync(customerId, Request("HOUSE", 40m, 3));

        var result = await fx.Service.GetAsync(customerId, created.AddressId);

        Assert.True(result.Success);
        Assert.Equal(created.AddressId, result.Data!.AddressId);
        Assert.Equal(120.00m, result.Data.TotalAreaM2);
        Assert.Equal("HOUSE", result.Data.HousingType);
    }

    [Fact]
    public async Task Create_for_a_removed_customer_returns_404()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();

        var result = await fx.Service.CreateAsync(int.MaxValue, Request());

        Assert.Equal(404, result.StatusCode);
    }

    // ---- delete (409 with orders, default re-election) ----

    [Fact]
    public async Task Delete_removes_a_non_default_address_and_keeps_the_default()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();
        var first = await fx.CreateAsync(customerId);
        var second = await fx.CreateAsync(customerId, Request(label: "Office"));

        var result = await fx.Service.DeleteAsync(customerId, second.AddressId);

        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
        Assert.Null(result.Data);
        var stored = Assert.Single(await StoredAsync(customerId));
        Assert.Equal(first.AddressId, stored.AddressId);
        Assert.True(stored.IsDefault);
    }

    [Fact]
    public async Task Deleting_the_default_makes_the_most_recently_created_remaining_address_default()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();
        var first = await fx.CreateAsync(customerId, Request(label: "First"));
        await fx.CreateAsync(customerId, Request(label: "Second"));
        var third = await fx.CreateAsync(customerId, Request(label: "Third"));

        var result = await fx.Service.DeleteAsync(customerId, first.AddressId);

        Assert.True(result.Success);
        var stored = await StoredAsync(customerId);
        Assert.Equal(2, stored.Count);
        Assert.Equal(third.AddressId, Assert.Single(stored, a => a.IsDefault).AddressId);
    }

    [Fact]
    public async Task Deleting_the_last_address_leaves_no_default()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();
        var only = await fx.CreateAsync(customerId);

        var result = await fx.Service.DeleteAsync(customerId, only.AddressId);

        Assert.True(result.Success);
        Assert.Empty(await StoredAsync(customerId));
    }

    [Fact]
    public async Task Delete_returns_409_while_an_order_references_the_address_and_works_after_the_order_is_gone()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();
        var address = await fx.CreateAsync(customerId);
        var orderId = await fx.AddOrderAsync(customerId, address.AddressId);

        var blocked = await fx.Service.DeleteAsync(customerId, address.AddressId);

        Assert.Equal(409, blocked.StatusCode);
        Assert.Single(await StoredAsync(customerId));

        await using (var db = CreateContext())
        {
            await db.JobOrders.Where(o => o.OrderId == orderId).ExecuteDeleteAsync();
        }

        var allowed = await fx.Service.DeleteAsync(customerId, address.AddressId);
        Assert.True(allowed.Success);
        Assert.Empty(await StoredAsync(customerId));
    }

    [Fact]
    public async Task Delete_of_a_referenced_default_does_not_move_the_default()
    {
        if (!IsSqlServerAvailable()) return;
        await using var fx = new Fixture();
        var customerId = await fx.AddCustomerAsync();
        var first = await fx.CreateAsync(customerId);
        await fx.CreateAsync(customerId, Request(label: "Office"));
        await fx.AddOrderAsync(customerId, first.AddressId);

        var result = await fx.Service.DeleteAsync(customerId, first.AddressId);

        Assert.Equal(409, result.StatusCode);
        var stored = await StoredAsync(customerId);
        Assert.Equal(first.AddressId, Assert.Single(stored, a => a.IsDefault).AddressId);
    }

    // ---- controller: ids from ICurrentUser, status mapping (no database) ----

    private sealed class RecordingAddressService : ICustomerAddressService
    {
        public int? LastCustomerId { get; private set; }
        public int? LastAddressId { get; private set; }
        public CustomerAddressResult<AddressDto> Single { get; set; } = CustomerAddressResult<AddressDto>.Ok(new AddressDto { AddressId = 9 });
        public CustomerAddressResult<IReadOnlyList<AddressDto>> Many { get; set; } =
            CustomerAddressResult<IReadOnlyList<AddressDto>>.Ok(new List<AddressDto>());
        public CustomerAddressResult<object?> Deletion { get; set; } = CustomerAddressResult<object?>.Ok(null);

        public Task<CustomerAddressResult<IReadOnlyList<AddressDto>>> ListAsync(int customerId, CancellationToken ct = default)
        {
            LastCustomerId = customerId;
            return Task.FromResult(Many);
        }

        public Task<CustomerAddressResult<AddressDto>> GetAsync(int customerId, int addressId, CancellationToken ct = default)
        {
            LastCustomerId = customerId;
            LastAddressId = addressId;
            return Task.FromResult(Single);
        }

        public Task<CustomerAddressResult<AddressDto>> CreateAsync(int customerId, AddressRequestDto request, CancellationToken ct = default)
        {
            LastCustomerId = customerId;
            return Task.FromResult(Single);
        }

        public Task<CustomerAddressResult<AddressDto>> UpdateAsync(int customerId, int addressId, AddressRequestDto request, CancellationToken ct = default)
        {
            LastCustomerId = customerId;
            LastAddressId = addressId;
            return Task.FromResult(Single);
        }

        public Task<CustomerAddressResult<object?>> DeleteAsync(int customerId, int addressId, CancellationToken ct = default)
        {
            LastCustomerId = customerId;
            LastAddressId = addressId;
            return Task.FromResult(Deletion);
        }
    }

    private sealed class TestUser(int? id) : ICurrentUser
    {
        public bool IsAuthenticated => id != null;
        public int? UserId => id;
        public UserRole? Role => id == null ? null : UserRole.Customer;
    }

    private static CustomerAddressesController CreateController(ICustomerAddressService service, int? userId) =>
        new(service, new TestUser(userId)) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    [Fact]
    public async Task Controller_uses_the_id_of_the_signed_in_customer_for_every_endpoint()
    {
        var service = new RecordingAddressService();
        var controller = CreateController(service, userId: 42);

        await controller.List(CancellationToken.None);
        Assert.Equal(42, service.LastCustomerId);

        service = new RecordingAddressService();
        controller = CreateController(service, userId: 43);
        await controller.Get(7, CancellationToken.None);
        Assert.Equal((43, 7), (service.LastCustomerId, service.LastAddressId));

        service = new RecordingAddressService();
        controller = CreateController(service, userId: 44);
        await controller.Create(Request(), CancellationToken.None);
        Assert.Equal(44, service.LastCustomerId);

        service = new RecordingAddressService();
        controller = CreateController(service, userId: 45);
        await controller.Update(8, Request(), CancellationToken.None);
        Assert.Equal((45, 8), (service.LastCustomerId, service.LastAddressId));

        service = new RecordingAddressService();
        controller = CreateController(service, userId: 46);
        await controller.Delete(9, CancellationToken.None);
        Assert.Equal((46, 9), (service.LastCustomerId, service.LastAddressId));
    }

    [Fact]
    public async Task Controller_returns_201_for_create_and_200_for_the_rest()
    {
        var controller = CreateController(new RecordingAddressService
        {
            Single = CustomerAddressResult<AddressDto>.Created(new AddressDto { AddressId = 1 })
        }, userId: 5);

        var created = await controller.Create(Request(), CancellationToken.None);

        var createdResult = Assert.IsType<ObjectResult>(created);
        Assert.Equal(StatusCodes.Status201Created, createdResult.StatusCode);
        Assert.True(Assert.IsType<ApiResponse<AddressDto>>(createdResult.Value).Success);

        var list = await CreateController(new RecordingAddressService(), 5).List(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(list); // an empty list is still a 200
        Assert.Empty(Assert.IsType<ApiResponse<IReadOnlyList<AddressDto>>>(ok.Value).Data!);

        var deleted = await CreateController(new RecordingAddressService(), 5).Delete(1, CancellationToken.None);
        var deleteBody = Assert.IsType<ApiResponse<object?>>(Assert.IsType<OkObjectResult>(deleted).Value);
        Assert.True(deleteBody.Success);
        Assert.Null(deleteBody.Data);
    }

    [Fact]
    public async Task Controller_maps_400_with_errors_map_404_and_409()
    {
        var service = new RecordingAddressService
        {
            Single = CustomerAddressResult<AddressDto>.ValidationError(new Dictionary<string, string[]> { ["label"] = ["required"] })
        };
        var controller = CreateController(service, userId: 5);

        var bad = await controller.Create(new AddressRequestDto(), CancellationToken.None);
        var badBody = Assert.IsType<ApiResponse<object>>(Assert.IsType<BadRequestObjectResult>(bad).Value);
        Assert.False(badBody.Success);
        Assert.NotNull(badBody.Data);

        service.Single = CustomerAddressResult<AddressDto>.NotFound();
        Assert.IsType<NotFoundObjectResult>(await controller.Get(1, CancellationToken.None));
        Assert.IsType<NotFoundObjectResult>(await controller.Update(1, Request(), CancellationToken.None));

        service.Deletion = CustomerAddressResult<object?>.NotFound();
        Assert.IsType<NotFoundObjectResult>(await controller.Delete(1, CancellationToken.None));

        service.Deletion = CustomerAddressResult<object?>.Conflict("in use");
        var conflict = await controller.Delete(1, CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(conflict);
    }

    [Fact]
    public async Task Controller_returns_401_for_every_endpoint_when_there_is_no_customer_id()
    {
        var service = new RecordingAddressService();
        var controller = CreateController(service, userId: null);

        Assert.IsType<UnauthorizedObjectResult>(await controller.List(CancellationToken.None));
        Assert.IsType<UnauthorizedObjectResult>(await controller.Get(1, CancellationToken.None));
        Assert.IsType<UnauthorizedObjectResult>(await controller.Create(Request(), CancellationToken.None));
        Assert.IsType<UnauthorizedObjectResult>(await controller.Update(1, Request(), CancellationToken.None));
        Assert.IsType<UnauthorizedObjectResult>(await controller.Delete(1, CancellationToken.None));
        Assert.Null(service.LastCustomerId);
    }

    [Fact]
    public async Task Controller_returns_400_when_the_body_is_missing()
    {
        var controller = CreateController(new RecordingAddressService(), userId: 5);

        Assert.IsType<BadRequestObjectResult>(await controller.Create(null!, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Update(1, null!, CancellationToken.None));
    }

    // ---- authorization and registration ----

    [Fact]
    public void Controller_requires_the_CustomerOnly_policy_so_other_roles_get_403_and_anonymous_401()
    {
        var attribute = typeof(CustomerAddressesController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("CustomerOnly", attribute!.Policy); // the policy itself is covered by CustomerOnly_policy_allows_only_authenticated_customers
    }

    [Fact]
    public void Controller_routes_match_the_contract()
    {
        Assert.Equal("api/customers/me/addresses", typeof(CustomerAddressesController).GetCustomAttribute<RouteAttribute>()!.Template);
    }

    [Fact]
    public void CustomersModule_registers_the_address_repository_and_service()
    {
        var services = new ServiceCollection();

        new CustomersModule().ConfigureServices(services, new ConfigurationBuilder().Build());

        Assert.Contains(services, d => d.ServiceType == typeof(ICustomerAddressService)
                                       && d.ImplementationType == typeof(CustomerAddressService));
        Assert.Contains(services, d => d.ServiceType == typeof(CommonService.Application.Features.Customers.ICustomerAddressRepository)
                                       && d.ImplementationType == typeof(CustomerAddressRepository));
    }
}
