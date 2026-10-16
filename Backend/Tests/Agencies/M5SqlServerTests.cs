using CommonService.Application.Common.Options;
using CommonService.Application.Features.Agencies;
using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modules.Agencies;
using CommonService.Infrastructure.Modules.Dispatch;
using CommonService.Infrastructure.Modules.Payments;
using CommonService.Infrastructure.Persistence;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

namespace CommonService.Tests.Agencies;

/// <summary>
/// The M5 repositories against a real SQL Server (conditional updates, unique indexes, enum conversion, GROUP BY, CHECK constraints).
/// Connection: env var <c>M5_SQL</c>, default LocalDB database <c>M5Verify</c> with every migration applied
/// (<c>dotnet ef database update --connection ...</c>). Without a reachable server each test returns at once.
/// </summary>
public class M5SqlServerTests
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("M5_SQL")
        ?? @"Server=(localdb)\MSSQLLocalDB;Database=M5Verify;Integrated Security=True;TrustServerCertificate=True;";

    private static readonly DateTime Now = new(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc);
    // A day of its own per test (xUnit builds one instance per test): leftovers of earlier runs in a shared database must not take part in the capacity search.
    private readonly DateOnly Day = DateOnly.FromDayNumber(new DateOnly(2026, 10, 15).DayNumber + Random.Shared.Next(1, 30_000));
    private const string Morning = "SHIFT_MORNING";

    private static bool Available()
    {
        try
        {
            using var conn = new SqlConnection(ConnectionString + "Connect Timeout=3;");
            conn.Open();
            using var cmd = new SqlCommand("SELECT COUNT(*) FROM AGENCY_SWAP_WINDOW", conn);
            cmd.ExecuteScalar();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);

    private static FakeClock Clock()
    {
        var c = new FakeClock();
        c.Set(Now);
        return c;
    }

    private static string Unique(string prefix, int length = 10) => (prefix + Guid.NewGuid().ToString("N"))[..length];

    private static string Digits(int n)
    {
        var s = new string(Guid.NewGuid().ToByteArray().Select(b => (char)('0' + (b % 10))).ToArray());
        return s[..n];
    }

    private sealed class World
    {
        public int AgencyId;
        public int CustomerId;
        public int AddressId;
        public List<int> WorkerIds = [];
        public List<int> SlotIds = [];
    }

    /// <summary>One ACTIVE agency (sla 100), a customer with an address, <paramref name="workers"/> agency workers each with an AVAILABLE slot.</summary>
    private async Task<World> SeedAsync(int workers = 2, string agencyStatus = "ACTIVE", decimal sla = 100m, decimal escrow = 5_000_000m, DateOnly? day = null)
    {
        await using var db = NewDb();
        var w = new World();
        var agency = new PartnerAgency
        {
            TaxCode = Digits(10),
            LegalName = "Test Agency " + Unique("a", 6),
            LegalRepresentative = "Rep",
            ContactPhone = "09" + Digits(8),
            ContactEmail = Unique("x", 8) + "@example.test",
            EscrowDepositBalance = escrow,
            SlaScore = sla,
            WorkerQuota = 3,
            BankAccountNo = "123",
            BankName = "VCB",
            AgencyStatus = agencyStatus,
            CreatedAt = Now,
        };
        db.PartnerAgencies.Add(agency);
        var customer = new Customer
        {
            PhoneNumber = "09" + Digits(8),
            FullName = "C",
            AccountStatus = CustomerAccountStatus.Active,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        w.AgencyId = agency.AgencyId;
        w.CustomerId = customer.CustomerId;

        var address = new CustomerAddress
        {
            CustomerId = customer.CustomerId,
            Label = "Home",
            AddressLine = "1 Test St",
            District = "D1",
            City = "HCMC",
            HousingType = HousingType.House,
            FloorAreaM2 = 40m,
            NumFloors = 2,
            Latitude = 10.76m,
            Longitude = 106.66m,
            IsDefault = true,
            CreatedAt = Now,
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();
        w.AddressId = address.AddressId;

        for (var i = 0; i < workers; i++)
        {
            var worker = Worker.CreateAgencyStaff(agency.AgencyId, "09" + Digits(8), Digits(12), "Worker " + i);
            worker.CreatedAt = Now.AddMinutes(i);
            worker.UpdatedAt = Now;
            db.Workers.Add(worker);
            await db.SaveChangesAsync();
            var slot = new BookingSlot
            {
                WorkerId = worker.WorkerId,
                AgencyId = agency.AgencyId,
                SlotDate = day ?? Day,
                ShiftCode = Morning,
                StartTime = new TimeOnly(8, 0),
                EndTime = new TimeOnly(12, 0),
                SlotSource = "AGENCY",
                SlotStatus = "AVAILABLE",
                UpdatedAt = Now,
            };
            db.BookingSlots.Add(slot);
            await db.SaveChangesAsync();
            w.WorkerIds.Add(worker.WorkerId);
            w.SlotIds.Add(slot.SlotId);
        }

        return w;
    }

    private async Task<long> NewOrderAsync(World w, ServiceTier tier = ServiceTier.Premium, byte workers = 1, DateOnly? day = null)
    {
        await using var db = NewDb();
        var order = new JobOrder
        {
            OrderCode = "T" + Digits(12),
            CustomerId = w.CustomerId,
            AddressId = w.AddressId,
            ServiceTier = tier,
            ScheduledDate = day ?? Day,
            ShiftCode = Morning,
            AreaSnapshotM2 = 50m,
            RequiredWorkers = workers,
            TotalAmount = 400000m,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        db.JobOrders.Add(order);
        await db.SaveChangesAsync();
        return order.OrderId;
    }

    private static IOptionsAdapter Rules() => new();

    private sealed class IOptionsAdapter
    {
        public Microsoft.Extensions.Options.IOptions<BusinessRules> Options { get; } = MicrosoftOptions.Create(new BusinessRules());
    }

    // ---- capacity (BE-M5-06) ---------------------------------------------------------------------

    [Fact]
    public async Task Capacity_holds_one_slot_for_exactly_one_of_many_concurrent_orders_and_release_frees_it()
    {
        if (!Available()) return;
        var w = await SeedAsync(workers: 1);
        var orderIds = new List<long>();
        for (var i = 0; i < 8; i++) orderIds.Add(await NewOrderAsync(w));

        var results = await Task.WhenAll(orderIds.Select(id => Task.Run(async () =>
        {
            await using var db = NewDb();
            var service = new AgencyCapacityService(new EfCapacityRepository(db, Clock(), Rules().Options), Clock(), Rules().Options);
            return (id, reservation: await service.TryReserveAsync(new CapacityRequest(Day, Morning, 1, id)));
        })));

        var winners = results.Where(r => r.reservation is not null).ToList();
        Assert.Single(winners);
        await using var check = NewDb();
        Assert.Equal("HELD", (await check.BookingSlots.SingleAsync(s => s.SlotId == w.SlotIds[0])).SlotStatus);
        Assert.Equal(1, await check.AgencyCapacityHolds.CountAsync(h => h.SlotId == w.SlotIds[0]));

        var repo = new EfCapacityRepository(check, Clock(), Rules().Options);
        var stored = await repo.GetByOrderAsync(winners[0].id);
        Assert.Equal(w.AgencyId, stored!.AgencyId);
        Assert.Equal(0.200m, stored.CommissionRate); // no subscription: FREE rate

        await repo.ReleaseByOrderAsync(winners[0].id, Now);
        await using var after = NewDb();
        Assert.Equal("AVAILABLE", (await after.BookingSlots.SingleAsync(s => s.SlotId == w.SlotIds[0])).SlotStatus);
        Assert.Null(await new EfCapacityRepository(after, Clock(), Rules().Options).GetByOrderAsync(winners[0].id));
    }

    [Fact]
    public async Task Verified_partner_label_follows_the_pro_package_with_its_grace_days()
    {
        if (!Available()) return;
        var inForce = await SeedAsync(1);
        var inGrace = await SeedAsync(1);
        var expired = await SeedAsync(1);
        var free = await SeedAsync(1);
        var today = new DateOnly(2026, 10, 14);
        await using (var db = NewDb())
        {
            var package = new SubscriptionPackage
            {
                PackageCode = Unique("V", 12),
                PackageName = "Pro test",
                Tier = "PRO",
                BillingCycle = "MONTHLY",
                Price = 1,
                WorkerQuota = 50,
                CommissionRate = 0m,
                HasRosterDashboard = true,
                HasAnalytics = true,
                PriorityDispatch = true,
                IsActive = true,
            };
            db.SubscriptionPackages.Add(package);
            await db.SaveChangesAsync();
            PartnerSubscription Sub(int agencyId, DateOnly end) => new()
            {
                AgencyId = agencyId,
                PackageId = package.PackageId,
                StartDate = end.AddDays(-30),
                EndDate = end,
                SubStatus = "ACTIVE",
                CreatedAt = Now,
            };
            db.PartnerSubscriptions.Add(Sub(inForce.AgencyId, today.AddDays(10)));
            db.PartnerSubscriptions.Add(Sub(inGrace.AgencyId, today.AddDays(-7)));   // ended 7 days ago: last grace day
            db.PartnerSubscriptions.Add(Sub(expired.AgencyId, today.AddDays(-8)));   // past the grace days
            var staleLabel = await db.PartnerAgencies.SingleAsync(a => a.AgencyId == expired.AgencyId);
            staleLabel.IsVerifiedPartner = true;
            await db.SaveChangesAsync();
        }

        await using var write = NewDb();
        var repo = new EfVerifiedPartnerRepository(write);
        // only this test's agencies are looked at through the agency filter
        foreach (var id in new[] { inForce.AgencyId, inGrace.AgencyId, expired.AgencyId, free.AgencyId })
        {
            await repo.SyncAsync(today, 7, id);
        }

        await using var read = NewDb();
        bool Label(int id) => read.PartnerAgencies.AsNoTracking().Single(a => a.AgencyId == id).IsVerifiedPartner;
        Assert.True(Label(inForce.AgencyId));
        Assert.True(Label(inGrace.AgencyId));
        Assert.False(Label(expired.AgencyId));
        Assert.False(Label(free.AgencyId));
    }

    [Fact]
    public async Task Agency_bank_account_is_stored_and_read_back()
    {
        if (!Available()) return;
        var w = await SeedAsync(1);

        await using (var db = NewDb())
        {
            var repo = new EfAgencyBankRepository(db);
            Assert.True(await repo.SetAsync(w.AgencyId, "Vietcombank", "0011001234567"));
            Assert.False(await repo.SetAsync(int.MaxValue, "X", "123456"));
        }

        await using var read = NewDb();
        var stored = await new EfAgencyBankRepository(read).GetAsync(w.AgencyId);
        Assert.Equal(("Vietcombank", "0011001234567"), stored);
        Assert.Null(await new EfAgencyBankRepository(read).GetAsync(int.MaxValue));
    }

    [Fact]
    public async Task Capacity_does_not_offer_the_slot_of_a_locked_worker()
    {
        if (!Available()) return;
        var w = await SeedAsync(2, day: new DateOnly(2026, 10, 17));
        await using (var db = NewDb())
        {
            var worker = await db.Workers.SingleAsync(x => x.WorkerId == w.WorkerIds[0]);
            worker.TransitionTo(WorkStatus.Locked);
            await db.SaveChangesAsync();
        }

        await using var read = NewDb();
        var open = await new EfCapacityRepository(read, Clock(), Rules().Options).GetOpenSlotsAsync(new DateOnly(2026, 10, 17), Morning, 50m);

        var ours = open.Where(o => o.AgencyId == w.AgencyId).Select(o => o.SlotId).ToList();
        Assert.Equal([w.SlotIds[1]], ours);
    }

    [Fact]
    public async Task Expiry_reminder_query_lists_the_ending_subscription_once_and_skips_a_renewed_one()
    {
        if (!Available()) return;
        var ending = await SeedAsync(1);
        var renewed = await SeedAsync(1);
        var today = new DateOnly(2026, 10, 14);
        await using (var db = NewDb())
        {
            var package = new SubscriptionPackage
            {
                PackageCode = Unique("R", 12),
                PackageName = "Pro test",
                Tier = "PRO",
                BillingCycle = "MONTHLY",
                Price = 1,
                WorkerQuota = 50,
                CommissionRate = 0m,
                HasRosterDashboard = true,
                HasAnalytics = true,
                PriorityDispatch = true,
                IsActive = true,
            };
            db.SubscriptionPackages.Add(package);
            await db.SaveChangesAsync();
            PartnerSubscription Sub(int agencyId, DateOnly start, DateOnly end) => new()
            {
                AgencyId = agencyId,
                PackageId = package.PackageId,
                StartDate = start,
                EndDate = end,
                SubStatus = "ACTIVE",
                CreatedAt = Now,
            };
            db.PartnerSubscriptions.Add(Sub(ending.AgencyId, today.AddDays(-20), today.AddDays(7)));
            db.PartnerSubscriptions.Add(Sub(renewed.AgencyId, today.AddDays(-20), today.AddDays(7)));
            db.PartnerSubscriptions.Add(Sub(renewed.AgencyId, today.AddDays(8), today.AddDays(38)));
            await db.SaveChangesAsync();
        }

        await using var read = NewDb();
        var repo = new EfSubscriptionReminderRepository(read);
        var list = await repo.ListEndingAsync(today, today.AddDays(7));

        var mine = Assert.Single(list, x => x.AgencyId == ending.AgencyId);
        Assert.Null(mine.ReminderDays);
        Assert.DoesNotContain(list, x => x.AgencyId == renewed.AgencyId);

        Assert.True(await repo.MarkRemindedAsync(mine.SubscriptionId, 7));
        await read.SaveChangesAsync();
        await using var after = NewDb();
        var again = await new EfSubscriptionReminderRepository(after).ListEndingAsync(today, today.AddDays(7));
        Assert.Equal(7, Assert.Single(again, x => x.AgencyId == ending.AgencyId).ReminderDays);
    }

    [Fact]
    public async Task Capacity_ignores_a_suspended_agency_a_low_sla_agency_and_uses_the_pro_commission()
    {
        if (!Available()) return;
        var suspended = await SeedAsync(1, agencyStatus: "SUSPENDED", day: new DateOnly(2026, 10, 16));
        var low = await SeedAsync(1, sla: 49.99m, day: new DateOnly(2026, 10, 16));
        var pro = await SeedAsync(1, day: new DateOnly(2026, 10, 16));
        await using (var db = NewDb())
        {
            var package = new SubscriptionPackage
            {
                PackageCode = Unique("P", 12),
                PackageName = "Pro test",
                Tier = "PRO",
                BillingCycle = "MONTHLY",
                Price = 1,
                WorkerQuota = 50,
                CommissionRate = 0m,
                HasRosterDashboard = true,
                HasAnalytics = true,
                PriorityDispatch = true,
                IsActive = true,
            };
            db.SubscriptionPackages.Add(package);
            await db.SaveChangesAsync();
            db.PartnerSubscriptions.Add(new PartnerSubscription
            {
                AgencyId = pro.AgencyId,
                PackageId = package.PackageId,
                StartDate = new DateOnly(2026, 10, 1),
                EndDate = new DateOnly(2026, 10, 31),
                SubStatus = "ACTIVE",
                CreatedAt = Now,
            });
            await db.SaveChangesAsync();
        }

        await using var read = NewDb();
        var open = await new EfCapacityRepository(read, Clock(), Rules().Options)
            .GetOpenSlotsAsync(new DateOnly(2026, 10, 16), Morning, 50m);

        var agencies = open.Select(s => s.AgencyId).ToHashSet();
        Assert.Contains(pro.AgencyId, agencies);
        Assert.DoesNotContain(suspended.AgencyId, agencies);
        Assert.DoesNotContain(low.AgencyId, agencies);
        var proSlot = open.Single(s => s.AgencyId == pro.AgencyId);
        Assert.Equal(0m, proSlot.CommissionRate);
        Assert.True(proSlot.PriorityDispatch);
    }

    // ---- escrow (BE-M5-07) -----------------------------------------------------------------------

    private static EscrowService NewEscrow(AppDbContext db, FakeCurrentUser user) =>
        new(new EfEscrowRepository(db), new PaymentRepository(db), new FakePaymentGateway(), new UnitOfWork(db), new FakeAuditLog(), user,
            Clock(), Rules().Options);

    [Fact]
    public async Task Escrow_penalty_deducts_records_the_ledger_and_suspends_at_zero_and_a_repeat_is_ignored()
    {
        if (!Available()) return;
        var w = await SeedAsync(0, escrow: 500_000m);
        var orderId = await NewOrderAsync(w);
        var user = new FakeCurrentUser();
        await using var db = NewDb();
        var escrow = NewEscrow(db, user);
        var request = new SlaPenaltyRequest(w.AgencyId, SlaViolation.NoShow, orderId, null, 300_000m, 0m, "no show");

        var first = await escrow.ApplyAsync(request);
        var again = await escrow.ApplyAsync(request);
        var second = await escrow.ApplyAsync(request with { OrderId = await NewOrderAsync(w), CustomerRefundAmount = 300_000m });

        Assert.Equal(300_000m, first.EscrowDeducted);
        Assert.Equal(first.EscrowDeducted, again.EscrowDeducted);
        Assert.Equal(200_000m, second.EscrowDeducted);
        Assert.Equal(100_000m, second.Shortfall);
        Assert.True(second.AgencySuspended);
        await using var check = NewDb();
        var agency = await check.PartnerAgencies.SingleAsync(a => a.AgencyId == w.AgencyId);
        Assert.Equal(0m, agency.EscrowDepositBalance);
        Assert.Equal("SUSPENDED", agency.AgencyStatus);
        Assert.Equal(60m, agency.SlaScore);
        var ledger = await check.EscrowTransactions.Where(t => t.AgencyId == w.AgencyId).OrderBy(t => t.EscrowTxnId).ToListAsync();
        Assert.Equal(2, ledger.Count);
        Assert.All(ledger, t => Assert.Equal(EscrowTransactionType.Penalty, t.TxnType));
        Assert.Equal([200_000m, 0m], ledger.Select(t => t.BalanceAfter));
    }

    [Fact]
    public async Task Escrow_deposit_payment_passes_the_check_constraint_and_settles_once_through_the_real_repositories()
    {
        if (!Available()) return;
        var w = await SeedAsync(0, agencyStatus: "PENDING", escrow: 0m);
        var user = new FakeCurrentUser();
        user.SignIn(w.AgencyId, UserRole.Partner);
        await using var db = NewDb();
        var escrow = NewEscrow(db, user);
        var purchase = await escrow.StartDepositAsync(new DepositRequest(5_000_000m));

        await using var db2 = NewDb();
        var payments = new PaymentRepository(db2);
        var transaction = await payments.GetPaymentAsync(purchase.PaymentId);
        Assert.Equal(PaymentPurpose.EscrowDeposit, transaction!.Purpose);
        Assert.Equal(w.AgencyId, transaction.AgencyId);

        var settlement = new EscrowDepositSettlement(payments, NewEscrow(db2, user), new UnitOfWork(db2), Clock(), NullLogger<EscrowDepositSettlement>.Instance);
        Assert.True(await settlement.SettleSuccessAsync(transaction, wasExpired: false, "{}"));

        await using var db3 = NewDb();
        var again = await new PaymentRepository(db3).GetPaymentAsync(purchase.PaymentId);
        var second = new EscrowDepositSettlement(new PaymentRepository(db3), NewEscrow(db3, user), new UnitOfWork(db3), Clock(), NullLogger<EscrowDepositSettlement>.Instance);
        Assert.False(await second.SettleSuccessAsync(again!, wasExpired: false, "{}"));

        await using var check = NewDb();
        var agency = await check.PartnerAgencies.SingleAsync(a => a.AgencyId == w.AgencyId);
        Assert.Equal(5_000_000m, agency.EscrowDepositBalance);
        Assert.Equal("ACTIVE", agency.AgencyStatus);
        Assert.Equal(PaymentStatus.Success, (await check.PaymentTransactions.SingleAsync(p => p.PaymentId == purchase.PaymentId)).TxnStatus);
        Assert.Single(await check.EscrowTransactions.Where(t => t.AgencyId == w.AgencyId).ToListAsync());
    }

    [Fact]
    public async Task Escrow_appeal_reversal_restores_money_points_and_status_in_the_database()
    {
        if (!Available()) return;
        var w = await SeedAsync(0, escrow: 100_000m);
        var orderId = await NewOrderAsync(w);
        var user = new FakeCurrentUser();
        long txn;
        await using (var db = NewDb())
        {
            await NewEscrow(db, user).ApplyAsync(new SlaPenaltyRequest(w.AgencyId, SlaViolation.NoShow, orderId, null, 260_000m, 0m, "x"));
            txn = (await db.EscrowTransactions.SingleAsync(t => t.AgencyId == w.AgencyId)).EscrowTxnId;
        }

        user.SignIn(w.AgencyId, UserRole.Partner);
        int appealId;
        await using (var db = NewDb())
        {
            appealId = (await NewEscrow(db, user).AppealAsync(txn, new AppealRequest("customer was wrong"))).AppealId;
        }

        int adminId;
        await using (var db = NewDb())
        {
            var admin = new AdminAccount { Email = Unique("a", 8) + "@example.test", FullName = "Adm", PasswordHash = "x", AdminRole = "SUPER_ADMIN", IsActive = true, CreatedAt = Now };
            db.Admins.Add(admin);
            await db.SaveChangesAsync();
            adminId = admin.AdminId;
        }

        user.SignIn(adminId, UserRole.Admin);
        await using (var db = NewDb())
        {
            var decided = await NewEscrow(db, user).DecideAppealAsync(appealId, new AppealDecisionRequest(true, "ok"));
            Assert.Equal("ACCEPTED", decided.Status);
        }

        await using var check = NewDb();
        var agency = await check.PartnerAgencies.SingleAsync(a => a.AgencyId == w.AgencyId);
        Assert.Equal(100_000m, agency.EscrowDepositBalance);
        Assert.Equal(100m, agency.SlaScore);
        Assert.Equal(EscrowTransactionType.Reversal, (await check.EscrowTransactions.OrderByDescending(t => t.EscrowTxnId).FirstAsync(t => t.AgencyId == w.AgencyId)).TxnType);
    }

    // ---- subscriptions (BE-M5-02) and import (BE-M5-03) -------------------------------------------

    [Fact]
    public async Task Subscription_purchase_activates_by_ipn_and_the_package_in_force_is_read_with_its_join()
    {
        if (!Available()) return;
        var w = await SeedAsync(0);
        int packageId;
        await using (var db = NewDb())
        {
            var package = new SubscriptionPackage
            {
                PackageCode = Unique("PRO", 12),
                PackageName = "Pro test",
                Tier = "PRO",
                BillingCycle = "MONTHLY",
                Price = 2_000_000,
                WorkerQuota = 50,
                CommissionRate = 0m,
                HasRosterDashboard = true,
                HasAnalytics = true,
                PriorityDispatch = true,
                IsActive = true,
            };
            db.SubscriptionPackages.Add(package);
            await db.SaveChangesAsync();
            packageId = package.PackageId;
        }

        var user = new FakeCurrentUser();
        user.SignIn(w.AgencyId, UserRole.Partner);
        long paymentId;
        await using (var db = NewDb())
        {
            var service = new SubscriptionService(
                new EfSubscriptionRepository(db), new PaymentRepository(db), new FakePaymentGateway(), new UnitOfWork(db), new FakeAuditLog(),
                user, Clock(), Rules().Options);
            var purchase = await service.SubscribeAsync(new SubscribeRequest(packageId));
            paymentId = purchase.PaymentId;
            var again = await service.SubscribeAsync(new SubscribeRequest(packageId));
            Assert.Equal(purchase.PaymentId, again.PaymentId);
        }

        await using (var db = NewDb())
        {
            var payment = (await new PaymentRepository(db).GetPaymentAsync(paymentId))!;
            var settlement = new SubscriptionPaymentSettlement(
                new PaymentRepository(db), new EfSubscriptionRepository(db), new UnitOfWork(db), new NoopPublisher(), Clock());
            Assert.True(await settlement.SettleSuccessAsync(payment, false, "{}"));
        }

        await using var read = NewDb();
        var reader = new SubscriptionService(
            new EfSubscriptionRepository(read), new PaymentRepository(read), new FakePaymentGateway(), new UnitOfWork(read), new FakeAuditLog(),
            user, Clock(), Rules().Options);
        var state = await reader.GetMineAsync();
        Assert.Equal("PRO", state.Tier);
        Assert.Equal(50, state.WorkerQuota);
        Assert.Equal(new DateOnly(2026, 11, 13), state.Subscription!.EndDate);
    }

    private sealed class NoopPublisher : IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
    }

    [Fact]
    public async Task Import_commit_creates_agency_staff_in_one_transaction_and_existing_numbers_are_found()
    {
        if (!Available()) return;
        var w = await SeedAsync(0);
        await using (var db = NewDb())
        {
            db.PartnerAgencies.Single(a => a.AgencyId == w.AgencyId).GuaranteeSignedAt = Now;
            await db.SaveChangesAsync();
        }

        var phone1 = "09" + Digits(8);
        var phone2 = "09" + Digits(8);
        var id1 = Digits(12);
        var id2 = Digits(12);
        var bytes = CommonService.Application.Features.Payouts.XlsxWriter.Build(
            "workers",
            WorkerImportService.Columns,
            [new object?[] { "An", phone1, id1, "VCB", "111" }, new object?[] { "Binh", phone2, id2, null, null }]);

        var user = new FakeCurrentUser();
        user.SignIn(w.AgencyId, UserRole.Partner);
        await using (var db = NewDb())
        {
            var subs = new SubscriptionService(
                new EfSubscriptionRepository(db), new PaymentRepository(db), new FakePaymentGateway(), new UnitOfWork(db), new FakeAuditLog(),
                user, Clock(), Rules().Options);
            var importer = new WorkerImportService(
                new EfWorkerImportRepository(db), subs, new FakeFileStorage(), new UnitOfWork(db), user, Clock(), NullLogger<WorkerImportService>.Instance);
            var result = await importer.CommitAsync(new MemoryStream(bytes));
            Assert.True(result.Committed);
        }

        await using var check = NewDb();
        var created = await check.Workers.Where(x => x.AgencyId == w.AgencyId).ToListAsync();
        Assert.Equal(2, created.Count);
        Assert.All(created, x => Assert.Equal(WorkStatus.Idle, x.WorkStatus));
        var repo = new EfWorkerImportRepository(check);
        Assert.Contains(phone1, await repo.FindExistingPhonesAsync([phone1, "0000000000"]));
        Assert.Contains(id2, await repo.FindExistingNationalIdsAsync([id2]));
    }

    // ---- roster (BE-M5-05) -----------------------------------------------------------------------

    [Fact]
    public async Task Roster_unique_index_rejects_a_duplicate_slot_and_a_concurrent_write_does_not_double_book()
    {
        if (!Available()) return;
        var w = await SeedAsync(1, day: new DateOnly(2026, 10, 20));
        var user = new FakeCurrentUser();
        user.SignIn(w.AgencyId, UserRole.Partner);
        var request = new RosterRequest([new RosterRequestEntry(w.WorkerIds[0], new DateOnly(2026, 10, 21), "SHIFT_AFTERNOON")]);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            await using var db = NewDb();
            var service = new ShiftRosterService(new EfRosterRepository(db), new UnitOfWork(db), user, Clock(), Rules().Options);
            try
            {
                await service.PutAsync(request);
                return "ok";
            }
            catch (Exception ex)
            {
                return ex.GetType().Name;
            }
        })));

        await using var check = NewDb();
        Assert.Equal(1, await check.BookingSlots.CountAsync(s => s.WorkerId == w.WorkerIds[0] && s.SlotDate == new DateOnly(2026, 10, 21)));
        // Every attempt either succeeded or failed with a business error, never an unhandled database exception.
        Assert.DoesNotContain("DbUpdateException", outcomes);
        Assert.DoesNotContain("InvalidOperationException", outcomes);
    }

    // ---- swap window (BE-M5-09) ------------------------------------------------------------------

    [Fact]
    public async Task Swap_moves_assignment_and_slots_atomically_and_the_window_closes_once()
    {
        if (!Available()) return;
        var w = await SeedAsync(2, day: new DateOnly(2026, 10, 22));
        var orderId = await NewOrderAsync(w, day: new DateOnly(2026, 10, 22));
        long assignmentId;
        await using (var db = NewDb())
        {
            var slot = await db.BookingSlots.SingleAsync(s => s.SlotId == w.SlotIds[0]);
            slot.SlotStatus = "BOOKED";
            var assignment = new JobAssignment
            {
                OrderId = orderId,
                CustomerId = w.CustomerId,
                WorkerId = w.WorkerIds[0],
                AgencyId = w.AgencyId,
                SlotId = w.SlotIds[0],
                ServiceTier = ServiceTier.Premium,
                AssignmentSeq = 1,
                DispatchRadiusKm = 5,
                GrossAmount = 400000m,
                CommissionRate = 0.2m,
                PayoutAmount = 320000m,
                CreatedAt = Now,
                UpdatedAt = Now,
            };
            assignment.TransitionTo(JobAssignmentStatus.Assigned);
            db.JobAssignments.Add(assignment);
            await db.SaveChangesAsync();
            assignmentId = assignment.AssignmentId;
        }

        var user = new FakeCurrentUser();
        user.SignIn(w.AgencyId, UserRole.Partner);
        var publisher = new NoopPublisher();
        await using (var db = NewDb())
        {
            var service = new AgencySwapService(new EfAgencySwapRepository(db), new UnitOfWork(db), publisher, user, Clock());
            var window = await service.ReportUnavailableAsync(assignmentId, new UnavailableRequest("sick"));
            Assert.Equal("OPEN", window.State);
            var swapped = await service.SwapWorkerAsync(assignmentId, new SwapRequest(w.WorkerIds[1]));
            Assert.Equal("SWAPPED", swapped.State);
        }

        await using var check = NewDb();
        var after = await check.JobAssignments.SingleAsync(a => a.AssignmentId == assignmentId);
        Assert.Equal(w.WorkerIds[1], after.WorkerId);
        Assert.Equal(w.SlotIds[1], after.SlotId);
        Assert.Equal("BOOKED", (await check.BookingSlots.SingleAsync(s => s.SlotId == w.SlotIds[1])).SlotStatus);
        Assert.Equal("AVAILABLE", (await check.BookingSlots.SingleAsync(s => s.SlotId == w.SlotIds[0])).SlotStatus);
        var closed = await check.AgencySwapWindows.SingleAsync(x => x.AssignmentId == assignmentId);
        Assert.Equal("SWAPPED", closed.Status);
        Assert.False(await new EfAgencySwapRepository(check).TryCloseWindowAsync(assignmentId, "EXPIRED", null, Now));
    }

    // ---- queries (BE-M5-08) ----------------------------------------------------------------------

    [Fact]
    public async Task Agency_report_group_by_and_job_list_translate_to_sql_and_count_correctly()
    {
        if (!Available()) return;
        var w = await SeedAsync(2, day: new DateOnly(2026, 10, 23));
        await using (var db = NewDb())
        {
            foreach (var (workerIndex, status) in new[] { (0, "COMPLETED"), (0, "COMPLETED"), (0, "CANCELLED_BY_WORKER"), (1, "COMPLETED") })
            {
                var orderId = await NewOrderAsync(w, day: new DateOnly(2026, 10, 23));
                var slot = new BookingSlot
                {
                    WorkerId = w.WorkerIds[workerIndex],
                    AgencyId = w.AgencyId,
                    SlotDate = new DateOnly(2026, 10, 24 + Random.Shared.Next(0, 3000) % 1),
                    ShiftCode = "SHIFT_EVENING",
                    StartTime = new TimeOnly(17, 30),
                    EndTime = new TimeOnly(20, 30),
                    SlotSource = "AGENCY",
                    SlotStatus = "BOOKED",
                    UpdatedAt = Now,
                };
                slot.SlotDate = DateOnly.FromDayNumber(Day.DayNumber + 30 + Random.Shared.Next(0, 5000));
                db.BookingSlots.Add(slot);
                await db.SaveChangesAsync();
                var assignment = new JobAssignment
                {
                    OrderId = orderId,
                    CustomerId = w.CustomerId,
                    WorkerId = w.WorkerIds[workerIndex],
                    AgencyId = w.AgencyId,
                    SlotId = slot.SlotId,
                    ServiceTier = ServiceTier.Premium,
                    AssignmentSeq = 1,
                    DispatchRadiusKm = 5,
                    GrossAmount = 400000m,
                    CommissionRate = 0.2m,
                    PayoutAmount = 320000m,
                    CreatedAt = Now,
                    UpdatedAt = Now,
                };
                assignment.TransitionTo(JobAssignmentStatus.Assigned);
                if (status == "COMPLETED")
                {
                    assignment.TransitionTo(JobAssignmentStatus.CheckedIn);
                    assignment.TransitionTo(JobAssignmentStatus.InProgress);
                    assignment.TransitionTo(JobAssignmentStatus.AwaitingAcceptance);
                    assignment.TransitionTo(JobAssignmentStatus.Completed);
                }
                else
                {
                    assignment.TransitionTo(JobAssignmentStatus.CancelledByWorker);
                }

                db.JobAssignments.Add(assignment);
                await db.SaveChangesAsync();
            }
        }

        await using var read = NewDb();
        var repo = new EfAgencyReportRepository(read);
        var rows = await repo.PerformanceAsync(w.AgencyId, Now.AddDays(-1), Now.AddDays(1));
        var first = rows.Single(r => r.WorkerId == w.WorkerIds[0]);
        Assert.Equal(3, first.Assigned);
        Assert.Equal(2, first.Completed);
        Assert.Equal(1, first.Cancelled);
        Assert.Equal(640_000m, first.PayoutTotal);
        Assert.Equal("Worker 1", rows.Single(r => r.WorkerId == w.WorkerIds[1]).WorkerName);

        var (items, total) = await repo.ListJobsAsync(w.AgencyId, JobAssignmentStatus.Completed, 0, 10);
        Assert.Equal(3, total);
        Assert.Equal(3, items.Count);
    }

    // ---- dispatch fallback (#310) ----------------------------------------------------------------

    [Fact]
    public async Task Fallback_status_move_stores_awaiting_fallback_and_the_waiting_orders_are_found()
    {
        if (!Available()) return;
        var w = await SeedAsync(1, day: new DateOnly(2026, 10, 25));
        var orderId = await NewOrderAsync(w, ServiceTier.Economy, workers: 2, day: new DateOnly(2026, 10, 25));
        await using (var db = NewDb())
        {
            var order = await db.JobOrders.SingleAsync(o => o.OrderId == orderId);
            order.TransitionTo(JobOrderStatus.Paid);
            order.TransitionTo(JobOrderStatus.Dispatching);
            await db.SaveChangesAsync();
        }

        await using var write = NewDb();
        var repo = new EfDispatchRepository(write);
        Assert.True(await repo.TryMoveOrderStatusAsync(orderId, JobOrderStatus.Dispatching, JobOrderStatus.AwaitingFallback, Now));
        Assert.False(await repo.TryMoveOrderStatusAsync(orderId, JobOrderStatus.Dispatching, JobOrderStatus.AwaitingFallback, Now));

        await using var read = NewDb();
        var waiting = await new EfDispatchRepository(read).GetAwaitingFallbackOrdersAsync();
        Assert.Contains(waiting, o => o.OrderId == orderId && o.OrderStatus == JobOrderStatus.AwaitingFallback);
        var raw = await read.Database.SqlQuery<string>($"SELECT order_status AS Value FROM JOB_ORDER WHERE order_id = {orderId}").SingleAsync();
        Assert.Equal("AWAITING_FALLBACK", raw);

        var slot = await new EfDispatchRepository(read).GetAvailableSlotAsync(w.WorkerIds[0], new DateOnly(2026, 10, 25), Morning);
        Assert.Equal(w.SlotIds[0], slot!.SlotId);
    }
}
