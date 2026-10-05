using CommonService.Application.Common.Options;
using CommonService.Application.Features.Customers.Dtos;
using CommonService.Application.Features.Customers.Services;
using CommonService.Application.Features.Identity.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modules.Customers;
using CommonService.Infrastructure.Modules.Identity;
using CommonService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CommonService.Tests.Customers;

/// <summary>
/// BE-M1-07: one flow across the Identity and Customers modules. A new phone logs in with an OTP, the token carries the
/// new customer's id and role, and that id drives the profile, address book and favorite-workers services
/// (contracts identity.md §2.2, customers.md §2). DB-backed against the local SQL Server; every row is deleted afterwards.
/// </summary>
public class IdentityToCustomersFlowTests
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

    private sealed class TestClock(DateTime initialUtc) : IClock
    {
        public DateTime UtcNow { get; set; } = initialUtc;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => local.AddHours(-7);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(UtcNow));
    }

    private sealed class CapturingOtpSender : IOtpSender
    {
        public string? LastCode { get; private set; }

        public Task SendAsync(string phoneNumber, string code, CancellationToken cancellationToken = default)
        {
            LastCode = code;
            return Task.CompletedTask;
        }
    }

    private sealed class Flow : IAsyncDisposable
    {
        private readonly List<string> _phones = [];
        private readonly List<int> _workerIds = [];

        public AppDbContext Db { get; } =
            new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);

        public TestClock Clock { get; } = new(new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc));
        public BusinessRules Rules { get; } = new();
        public CapturingOtpSender Sender { get; } = new();
        public FakeWorkerProfileQuery WorkerPort { get; } = new();
        public TokenService Tokens { get; }
        public OtpService Otp { get; }
        public CustomerProfileService Profiles { get; }
        public CustomerAddressService Addresses { get; }
        public CustomerFavoriteWorkerService Favorites { get; }

        public Flow()
        {
            var options = Microsoft.Extensions.Options.Options.Create(Rules);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Otp:HmacSecret"] = "Test_Hmac_Secret_At_Least_32_Characters_Long!",
                ["Jwt:Key"] = "Test_Jwt_Secret_Key_At_Least_32_Characters_Long!"
            }).Build();

            var customers = new CustomerRepository(Db);
            var unitOfWork = new UnitOfWork(Db);
            Tokens = new TokenService(options, config);
            Otp = new OtpService(Db, Sender, Clock, options, config, Tokens, NullLogger<OtpService>.Instance);
            Profiles = new CustomerProfileService(customers, unitOfWork, Clock);
            Addresses = new CustomerAddressService(customers, new CustomerAddressRepository(Db), unitOfWork, Clock, options);
            Favorites = new CustomerFavoriteWorkerService(customers, new FavoriteWorkerRepository(Db), WorkerPort, Clock);
        }

        /// <summary>Full OTP login of a new phone: request a code, verify it, return the auth result.</summary>
        public async Task<AuthResult> LoginNewCustomerAsync()
        {
            var phone = $"09{Random.Shared.Next(10000000, 99999999)}";
            _phones.Add(phone);
            var ip = $"10.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}";

            var request = await Otp.RequestOtpAsync(phone, "Customer", ip);
            Assert.True(request.Success, request.ErrorMessage);

            var verify = await Otp.VerifyOtpAsync(phone, "Customer", Sender.LastCode!);
            Assert.True(verify.Success, verify.ErrorMessage);
            return new AuthResult(phone, verify.AuthResult!);
        }

        public async Task<int> AddWorkerAsync(string name, decimal rating, int completedJobs, WorkStatus status)
        {
            var now = Clock.UtcNow;
            var worker = Worker.CreateFreelancer(
                $"03{Random.Shared.Next(10000000, 99999999)}",
                Random.Shared.NextInt64(100_000_000_000, 999_999_999_999).ToString(),
                name);
            worker.KycStatus = "APPROVED";
            worker.RatingAvg = rating;
            worker.CompletedJobs = completedJobs;
            worker.CreatedAt = now;
            worker.UpdatedAt = now;
            Db.Workers.Add(worker);
            await Db.SaveChangesAsync();
            Db.Entry(worker).State = EntityState.Detached;
            _workerIds.Add(worker.WorkerId);
            WorkerPort.Add(new CommonService.Application.Interfaces.Ports.WorkerProfileSummary(
                worker.WorkerId, name, rating, completedJobs, status));
            return worker.WorkerId;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var phone in _phones)
            {
                var ids = await Db.Customers.Where(c => c.PhoneNumber == phone).Select(c => c.CustomerId).ToListAsync();
                foreach (var id in ids)
                {
                    await Db.FavoriteWorkers.Where(f => f.CustomerId == id).ExecuteDeleteAsync();
                    await Db.CustomerAddresses.Where(a => a.CustomerId == id).ExecuteDeleteAsync();
                    await Db.RefreshTokens
                        .Where(t => t.SubjectRole == UserRole.Customer && t.SubjectId == id)
                        .ExecuteDeleteAsync();
                }

                await Db.Customers.Where(c => c.PhoneNumber == phone).ExecuteDeleteAsync();
                await Db.OtpCodes.Where(o => o.PhoneNumber == phone).ExecuteDeleteAsync();
            }

            foreach (var id in _workerIds)
            {
                await Db.FavoriteWorkers.Where(f => f.WorkerId == id).ExecuteDeleteAsync();
                await Db.Workers.Where(w => w.WorkerId == id).ExecuteDeleteAsync();
            }

            await Db.DisposeAsync();
        }
    }

    private sealed record AuthResult(string Phone, CommonService.Application.Features.Identity.Dtos.AuthResultDto Auth);

    private static AddressRequestDto House(string label) => new()
    {
        Label = label,
        AddressLine = "12 Nguyen Hue",
        District = "District 1",
        City = "Ho Chi Minh City",
        HousingType = "HOUSE",
        FloorAreaM2 = 40m,
        NumFloors = 3,
        Latitude = 10.776889m,
        Longitude = 106.700806m
    };

    [Fact]
    public async Task A_new_phone_logs_in_and_its_token_id_drives_profile_addresses_and_favorites()
    {
        if (!IsSqlServerAvailable()) return;
        await using var flow = new Flow();

        // 1. OTP login creates the customer; the token carries that customer's id and role.
        var login = await flow.LoginNewCustomerAsync();
        var user = login.Auth.User;
        Assert.True(user.IsNewUser);
        Assert.Equal("Customer", user.Role);

        var principal = flow.Tokens.ValidateAccessToken(login.Auth.AccessToken);
        Assert.NotNull(principal);
        Assert.Equal(user.Id.ToString(), principal!.FindFirst("sub")?.Value);
        Assert.Equal("Customer", principal.FindFirst("role")?.Value);

        // 2. The id from the token reads the profile created by the login (decisions Q21 C1 / C2).
        var profile = (await flow.Profiles.GetAsync(user.Id)).Data!;
        Assert.Equal(user.Id, profile.CustomerId);
        Assert.Equal(login.Phone, profile.PhoneNumber);
        Assert.Equal("", profile.FullName);
        Assert.Equal(flow.Rules.Customer.InitialTrustScore, profile.TrustScore);
        Assert.Equal("ACTIVE", profile.AccountStatus);

        // 3. The profile can be completed.
        var updated = await flow.Profiles.UpdateAsync(user.Id,
            new UpdateCustomerProfileDto { FullName = "Nguyen Van A", Email = "a@test.local" });
        Assert.True(updated.Success);
        Assert.Equal("Nguyen Van A", (await flow.Profiles.GetAsync(user.Id)).Data!.FullName);

        // 4. An address is created for the same id with the server-computed S_total and becomes the default.
        var address = await flow.Addresses.CreateAsync(user.Id, House("Home"));
        Assert.True(address.Success);
        Assert.Equal(120.00m, address.Data!.TotalAreaM2);
        Assert.True(address.Data.IsDefault);

        // 5. A worker is added to the favorites with data from the port.
        var workerId = await flow.AddWorkerAsync("Tran Thi B", 4.85m, 60, WorkStatus.Idle);
        var favorite = await flow.Favorites.AddAsync(user.Id, workerId);
        Assert.True(favorite.Success);
        var favorites = (await flow.Favorites.ListAsync(user.Id)).Data!;
        var entry = Assert.Single(favorites);
        Assert.Equal("Tran Thi B", entry.FullName);
        Assert.Equal("IDLE", entry.WorkStatus);
    }

    [Fact]
    public async Task A_second_customer_sees_nothing_of_the_first_customers_data()
    {
        if (!IsSqlServerAvailable()) return;
        await using var flow = new Flow();
        var first = (await flow.LoginNewCustomerAsync()).Auth.User;
        var second = (await flow.LoginNewCustomerAsync()).Auth.User;
        Assert.NotEqual(first.Id, second.Id);

        await flow.Profiles.UpdateAsync(first.Id, new UpdateCustomerProfileDto { FullName = "First Customer" });
        var firstAddress = (await flow.Addresses.CreateAsync(first.Id, House("First home"))).Data!;
        var workerId = await flow.AddWorkerAsync("Shared Worker", 4.5m, 10, WorkStatus.Busy);
        await flow.Favorites.AddAsync(first.Id, workerId);

        Assert.Equal("", (await flow.Profiles.GetAsync(second.Id)).Data!.FullName);
        Assert.Empty((await flow.Addresses.ListAsync(second.Id)).Data!);
        Assert.Equal(404, (await flow.Addresses.GetAsync(second.Id, firstAddress.AddressId)).StatusCode);
        Assert.Empty((await flow.Favorites.ListAsync(second.Id)).Data!);

        // the first customer still has everything
        Assert.Single((await flow.Addresses.ListAsync(first.Id)).Data!);
        Assert.Single((await flow.Favorites.ListAsync(first.Id)).Data!);
    }

    [Fact]
    public async Task Logging_in_again_returns_the_same_customer_with_its_data_and_isNewUser_false()
    {
        if (!IsSqlServerAvailable()) return;
        await using var flow = new Flow();
        var first = await flow.LoginNewCustomerAsync();
        await flow.Profiles.UpdateAsync(first.Auth.User.Id, new UpdateCustomerProfileDto { FullName = "Returning Customer" });
        await flow.Addresses.CreateAsync(first.Auth.User.Id, House("Home"));

        flow.Clock.UtcNow = flow.Clock.UtcNow.AddMinutes(2); // resend cooldown of the same phone
        var request = await flow.Otp.RequestOtpAsync(first.Phone, "Customer", "10.9.9.9");
        Assert.True(request.Success, request.ErrorMessage);
        var second = await flow.Otp.VerifyOtpAsync(first.Phone, "Customer", flow.Sender.LastCode!);

        Assert.True(second.Success);
        Assert.False(second.AuthResult!.User.IsNewUser);
        Assert.Equal(first.Auth.User.Id, second.AuthResult.User.Id);
        Assert.Equal("Returning Customer", (await flow.Profiles.GetAsync(second.AuthResult.User.Id)).Data!.FullName);
        Assert.Single((await flow.Addresses.ListAsync(second.AuthResult.User.Id)).Data!);
    }
}
