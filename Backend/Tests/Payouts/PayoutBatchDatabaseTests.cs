using CommonService.Application.Features.Payouts;
using CommonService.Application.Features.Payouts.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Modules.Admin;
using CommonService.Infrastructure.Modules.Disputes;
using CommonService.Infrastructure.Modules.Payouts;
using CommonService.Infrastructure.Persistence;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Tests.Payouts;

/// <summary>
/// BE-M6-04 on the local SQL Server: the real queries, the month boundaries in Asia/Ho_Chi_Minh, the penalty source over resolved disputes,
/// the item locking and the application lock. The months used (2094-2098) are far in the future so no real data falls in them; the
/// clock is fixed in 2099 so they all count as finished. Every test removes what it seeded.
/// </summary>
public class PayoutBatchDatabaseTests
{
    private const string ConnectionString =
        "Server=localhost;Database=ChoThueGiupViec;Integrated Security=True;TrustServerCertificate=True;";

    private static readonly DateTime Now = new(2099, 6, 1, 4, 0, 0, DateTimeKind.Utc);

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

    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;

    private sealed class RecordingPublisher : IPublisher
    {
        public List<PayoutBatchClosed> Events { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            if (notification is PayoutBatchClosed e) Events.Add(e);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            Publish((object)notification, cancellationToken);
    }

    private static PayoutBatchService Real(AppDbContext db, RecordingPublisher? publisher = null) =>
        new(new EfPayoutRepository(db), new EfPayoutPenaltySource(db), new EfAuditLog(db, new PayoutBatchServiceTests.TestClock(Now)),
            new UnitOfWork(db), new PayoutBatchServiceTests.TestClock(Now), publisher ?? new RecordingPublisher());

    /// <summary>Everything one test seeded, so it can be removed.</summary>
    private sealed class Seed
    {
        public int AdminId { get; set; }
        public int CustomerId { get; set; }
        public int AddressId { get; set; }
        public int AgencyId { get; set; }
        public List<int> WorkerIds { get; } = [];
        public List<long> OrderIds { get; } = [];
        public List<int> SlotIds { get; } = [];
        public List<long> AssignmentIds { get; } = [];
        public List<int> BatchIds { get; } = [];
    }

    private static async Task<Seed> SeedBaseAsync(bool withAgency = false)
    {
        var seed = new Seed();
        await using var db = new AppDbContext(Options());

        var admin = new AdminAccount
        {
            Email = "pay-" + Guid.NewGuid().ToString("N")[..10] + "@example.test",
            FullName = "Payout Admin",
            PasswordHash = "x",
            AdminRole = "SUPER_ADMIN",
            IsActive = true,
            CreatedAt = Now,
        };
        db.Admins.Add(admin);

        var customer = new Customer
        {
            PhoneNumber = "091" + Random.Shared.Next(1000000, 9999999),
            FullName = "Payout Customer",
            AccountStatus = CustomerAccountStatus.Active,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

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

        seed.AdminId = admin.AdminId;
        seed.CustomerId = customer.CustomerId;
        seed.AddressId = address.AddressId;

        if (withAgency)
        {
            var agency = new PartnerAgency
            {
                TaxCode = "TX" + Guid.NewGuid().ToString("N")[..10],
                LegalName = "Payout Agency Co",
                LegalRepresentative = "Boss",
                ContactPhone = "093" + Random.Shared.Next(1000000, 9999999),
                ContactEmail = "agency-" + Guid.NewGuid().ToString("N")[..8] + "@example.test",
                BankAccountNo = "777888",
                BankName = "VCB",
                AgencyStatus = "ACTIVE",
                CreatedAt = Now,
            };
            db.PartnerAgencies.Add(agency);
            await db.SaveChangesAsync();
            seed.AgencyId = agency.AgencyId;
        }

        return seed;
    }

    private static async Task<int> AddWorkerAsync(Seed seed, string name, string? bankNo, bool agencyStaff = false)
    {
        await using var db = new AppDbContext(Options());
        var phone = "092" + Random.Shared.Next(1000000, 9999999);
        var national = "079" + Random.Shared.Next(100000000, 999999999);
        var worker = agencyStaff
            ? Worker.CreateAgencyStaff(seed.AgencyId, phone, national, name)
            : Worker.CreateFreelancer(phone, national, name);
        worker.BankAccountNo = bankNo;
        worker.BankName = bankNo is null ? null : "ACB";
        worker.CreatedAt = Now;
        worker.UpdatedAt = Now;
        db.Workers.Add(worker);
        await db.SaveChangesAsync();
        seed.WorkerIds.Add(worker.WorkerId);
        return worker.WorkerId;
    }

    private static int _dayCounter;

    /// <summary>One order, one slot and one assignment; COMPLETED at <paramref name="completedAtUtc"/> or ABSENT with a fee dated <paramref name="completedAtUtc"/>.</summary>
    private static async Task<long> AddAssignmentAsync(
        Seed seed, int workerId, DateTime completedAtUtc, decimal gross = 260000m, decimal rate = 0.200m, int? agencyId = null, decimal? absenceFee = null)
    {
        await using var db = new AppDbContext(Options());

        var order = new JobOrder
        {
            OrderCode = "ORD" + Random.Shared.Next(100000, 999999),
            CustomerId = seed.CustomerId,
            AddressId = seed.AddressId,
            ServiceTier = ServiceTier.Economy,
            ScheduledDate = new DateOnly(2090, 1, 1).AddDays(Interlocked.Increment(ref _dayCounter)),
            ShiftCode = "SHIFT1",
            AreaSnapshotM2 = 80m,
            RequiredWorkers = 1,
            TotalAmount = gross,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        db.JobOrders.Add(order);
        await db.SaveChangesAsync();

        var slot = new BookingSlot
        {
            WorkerId = workerId,
            SlotDate = order.ScheduledDate,
            ShiftCode = "SHIFT1",
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(12, 0),
            SlotSource = "MANUAL",
            SlotStatus = "LOCKED",
            UpdatedAt = Now,
        };
        db.BookingSlots.Add(slot);
        await db.SaveChangesAsync();

        var assignment = new JobAssignment
        {
            OrderId = order.OrderId,
            CustomerId = seed.CustomerId,
            WorkerId = workerId,
            AgencyId = agencyId,
            SlotId = slot.SlotId,
            ServiceTier = ServiceTier.Economy,
            AssignmentSeq = 1,
            DispatchRadiusKm = 5,
            GrossAmount = gross,
            CommissionRate = rate,
            PayoutAmount = gross - Math.Round(gross * rate),
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        assignment.TransitionTo(JobAssignmentStatus.CheckedIn);
        if (absenceFee is { } fee)
        {
            assignment.TransitionTo(JobAssignmentStatus.Absent);
            assignment.AbsenceFeeAmount = fee;
            assignment.UpdatedAt = completedAtUtc;
        }
        else
        {
            assignment.TransitionTo(JobAssignmentStatus.InProgress);
            assignment.TransitionTo(JobAssignmentStatus.AwaitingAcceptance);
            assignment.TransitionTo(JobAssignmentStatus.Completed);
            assignment.CompletedAt = completedAtUtc;
        }

        db.JobAssignments.Add(assignment);
        await db.SaveChangesAsync();

        seed.OrderIds.Add(order.OrderId);
        seed.SlotIds.Add(slot.SlotId);
        seed.AssignmentIds.Add(assignment.AssignmentId);
        return assignment.AssignmentId;
    }

    private static async Task AddResolvedDisputeAsync(long orderId, FaultParty fault, decimal compensation, DateTime resolvedAtUtc, string category = "QUALITY")
    {
        await using var db = new AppDbContext(Options());
        db.DisputeTickets.Add(new DisputeTicket
        {
            OrderId = orderId,
            RaisedBy = "CUSTOMER",
            Category = category,
            Description = "Dirty",
            DisputeStatus = "RESOLVED",
            FaultParty = fault,
            CompensationAmount = compensation,
            SlaDueAt = resolvedAtUtc,
            ResolvedAt = resolvedAtUtc,
            CreatedAt = resolvedAtUtc.AddHours(-1),
        });
        await db.SaveChangesAsync();
    }

    private static async Task CleanAsync(Seed seed, params string[] periods)
    {
        await using var db = new AppDbContext(Options());
        var batchIds = await db.PayoutBatches.Where(b => periods.Contains(b.PeriodMonth)).Select(b => b.BatchId).ToListAsync();
        var batchIdTexts = batchIds.Select(id => id.ToString()).ToList();

        await db.AdminAuditLogs.Where(a => a.EntityType == "PAYOUT_BATCH" && batchIdTexts.Contains(a.EntityId)).ExecuteDeleteAsync();
        await db.JobAssignments.Where(a => seed.AssignmentIds.Contains(a.AssignmentId)).ExecuteUpdateAsync(s => s.SetProperty(a => a.PayoutItemId, (int?)null));
        await db.PayoutItems.Where(i => batchIds.Contains(i.BatchId)).ExecuteDeleteAsync();
        await db.PayoutBatches.Where(b => batchIds.Contains(b.BatchId)).ExecuteDeleteAsync();
        await db.DisputeTickets.Where(d => seed.OrderIds.Contains(d.OrderId)).ExecuteDeleteAsync();
        await db.JobAssignments.Where(a => seed.AssignmentIds.Contains(a.AssignmentId)).ExecuteDeleteAsync();
        await db.BookingSlots.Where(b => seed.SlotIds.Contains(b.SlotId)).ExecuteDeleteAsync();
        await db.JobOrders.Where(o => seed.OrderIds.Contains(o.OrderId)).ExecuteDeleteAsync();
        await db.CustomerAddresses.Where(a => a.AddressId == seed.AddressId).ExecuteDeleteAsync();
        await db.Customers.Where(c => c.CustomerId == seed.CustomerId).ExecuteDeleteAsync();
        await db.Workers.Where(w => seed.WorkerIds.Contains(w.WorkerId)).ExecuteDeleteAsync();
        if (seed.AgencyId != 0) await db.PartnerAgencies.Where(a => a.AgencyId == seed.AgencyId).ExecuteDeleteAsync();
        await db.Admins.Where(a => a.AdminId == seed.AdminId).ExecuteDeleteAsync();
    }

    private static DateTime Utc(int y, int m, int d, int h = 0, int min = 0, int s = 0) => new(y, m, d, h, min, s, DateTimeKind.Utc);

    [Fact]
    public async Task Real_database_the_month_is_read_in_Ho_Chi_Minh_time_each_payee_gets_one_item_and_a_rebuild_gives_the_same_numbers()
    {
        if (!IsSqlServerAvailable()) return;

        var seed = await SeedBaseAsync(withAgency: true);
        const string period = "2098-02";
        try
        {
            var f = await AddWorkerAsync(seed, "Freelancer F", "111222");
            var g = await AddWorkerAsync(seed, "Freelancer G", null);
            var s = await AddWorkerAsync(seed, "Agency Staff S", "555", agencyStaff: true);

            // Boundaries of February 2098 in Asia/Ho_Chi_Minh (UTC+7): 2098-01-31T17:00Z .. 2098-02-28T17:00Z (exclusive).
            await AddAssignmentAsync(seed, f, Utc(2098, 1, 31, 16, 59, 59)); // 23:59:59 on 31 January: not February
            var feb1 = await AddAssignmentAsync(seed, f, Utc(2098, 1, 31, 17, 0, 0)); // 00:00:00 on 1 February: included
            var feb28 = await AddAssignmentAsync(seed, f, Utc(2098, 2, 28, 16, 59, 59)); // 23:59:59 on 28 February: included
            await AddAssignmentAsync(seed, f, Utc(2098, 2, 28, 17, 0, 0)); // 00:00:00 on 1 March: not February
            var absent = await AddAssignmentAsync(seed, f, Utc(2098, 2, 10, 3), absenceFee: 104000m);
            await AddAssignmentAsync(seed, g, Utc(2098, 2, 12, 3), gross: 100001m);
            await AddAssignmentAsync(seed, s, Utc(2098, 2, 14, 3), gross: 300000m, rate: 0.150m, agencyId: seed.AgencyId);
            await AddAssignmentAsync(seed, s, Utc(2098, 2, 15, 3), gross: 300000m, rate: 0.150m, agencyId: seed.AgencyId);

            int batchId;
            await using (var db = new AppDbContext(Options()))
            {
                var built = await Real(db).BuildAsync(period);
                Assert.Equal(201, built.StatusCode);
                Assert.Equal(3, built.Data!.ItemCount);
                Assert.Equal(520000m + 80001m + 510000m, built.Data.TotalAmount);
                batchId = built.Data.BatchId;
                seed.BatchIds.Add(batchId);
            }

            await using (var db = new AppDbContext(Options()))
            {
                var detail = (await Real(db).GetAsync(batchId, null, null, null)).Data!;
                Assert.Equal(["Freelancer F", "Freelancer G", "Payout Agency Co"], detail.Items.Select(i => i.PayeeName).ToArray());

                var itemF = detail.Items[0];
                Assert.Equal("FREELANCER", itemF.PayeeType);
                Assert.Equal(3, itemF.JobCount); // two completed jobs and the absence fee
                Assert.Equal(624000m, itemF.GrossAmount); // 260000 + 260000 + 104000
                Assert.Equal(104000m, itemF.CommissionAmount); // the absence fee carries no commission
                Assert.Equal(0m, itemF.PenaltyAmount);
                Assert.Equal(520000m, itemF.NetAmount);
                Assert.Equal("111222", itemF.BankAccountNo);
                Assert.Equal("ACB", itemF.BankName);
                Assert.Equal("PENDING", itemF.ItemStatus);

                var itemG = detail.Items[1];
                Assert.Equal(100001m, itemG.GrossAmount);
                Assert.Equal(20000m, itemG.CommissionAmount); // 20000.2 rounded
                Assert.Equal(80001m, itemG.NetAmount);
                Assert.Equal(string.Empty, itemG.BankAccountNo);

                var itemAgency = detail.Items[2];
                Assert.Equal("AGENCY", itemAgency.PayeeType);
                Assert.Null(itemAgency.WorkerId);
                Assert.Equal(seed.AgencyId, itemAgency.AgencyId);
                Assert.Equal(2, itemAgency.JobCount); // one agency item for both runs of its staff
                Assert.Equal(600000m, itemAgency.GrossAmount);
                Assert.Equal(90000m, itemAgency.CommissionAmount);
                Assert.Equal(510000m, itemAgency.NetAmount);
                Assert.Equal("777888", itemAgency.BankAccountNo);
                Assert.Equal("VCB", itemAgency.BankName);

                Assert.Equal(["Freelancer G has no bank account number."], detail.Warnings.ToArray());

                var onlyAgency = (await Real(db).GetAsync(batchId, "AGENCY", "1", "1")).Data!;
                Assert.Equal(1, onlyAgency.Total);
            }

            await using (var db = new AppDbContext(Options()))
            {
                // The six paid assignments (3 of F, 1 of G, 2 of the agency staff) carry an item id; the two out-of-month rows do not.
                var attached = await db.JobAssignments.AsNoTracking()
                    .Where(a => seed.AssignmentIds.Contains(a.AssignmentId) && a.PayoutItemId != null).CountAsync();
                Assert.Equal(6, attached);
                Assert.NotNull((await db.JobAssignments.AsNoTracking().SingleAsync(a => a.AssignmentId == feb1)).PayoutItemId);
                Assert.NotNull((await db.JobAssignments.AsNoTracking().SingleAsync(a => a.AssignmentId == feb28)).PayoutItemId);
                Assert.NotNull((await db.JobAssignments.AsNoTracking().SingleAsync(a => a.AssignmentId == absent)).PayoutItemId);
            }

            await using (var db = new AppDbContext(Options()))
            {
                var again = await Real(db).BuildAsync(period);
                Assert.Equal(200, again.StatusCode); // idempotent: the same batch, rebuilt
                Assert.Equal(batchId, again.Data!.BatchId);
                Assert.Equal(520000m + 80001m + 510000m, again.Data.TotalAmount);
                Assert.Equal(3, again.Data.ItemCount);
            }

            await using (var db = new AppDbContext(Options()))
            {
                Assert.Equal(1, await db.PayoutBatches.CountAsync(b => b.PeriodMonth == period));
                Assert.Equal(3, await db.PayoutItems.CountAsync(i => i.BatchId == batchId)); // no duplicates after the rebuild
                var attached = await db.JobAssignments.AsNoTracking()
                    .Where(a => seed.AssignmentIds.Contains(a.AssignmentId) && a.PayoutItemId != null).CountAsync();
                Assert.Equal(6, attached); // and no assignment counted twice
            }
        }
        finally
        {
            await CleanAsync(seed, period);
        }
    }

    [Fact]
    public async Task Real_database_a_new_late_assignment_joins_a_DRAFT_rebuild_and_a_CLOSED_batch_never_changes()
    {
        if (!IsSqlServerAvailable()) return;

        var seed = await SeedBaseAsync();
        const string period = "2097-05";
        try
        {
            var f = await AddWorkerAsync(seed, "Freelancer F", "111");
            await AddAssignmentAsync(seed, f, Utc(2097, 5, 10, 3));

            var publisher = new RecordingPublisher();
            int batchId;
            await using (var db = new AppDbContext(Options()))
            {
                batchId = (await Real(db, publisher).BuildAsync(period)).Data!.BatchId;
            }

            await AddAssignmentAsync(seed, f, Utc(2097, 5, 11, 3)); // appears after the first build

            await using (var db = new AppDbContext(Options()))
            {
                var rebuilt = await Real(db, publisher).BuildAsync(period);
                Assert.Equal(200, rebuilt.StatusCode);
                Assert.Equal(416000m, rebuilt.Data!.TotalAmount); // both assignments, 2 x 208000
            }

            await using (var db = new AppDbContext(Options()))
            {
                var closed = await Real(db, publisher).ConfirmAsync(seed.AdminId, batchId);
                Assert.True(closed.Success);
                Assert.Equal("CLOSED", closed.Data!.BatchStatus);
                Assert.Equal(seed.AdminId, closed.Data.ConfirmedBy);
                Assert.NotNull(closed.Data.ConfirmedAt);
            }

            await AddAssignmentAsync(seed, f, Utc(2097, 5, 12, 3)); // too late for a closed month

            await using (var db = new AppDbContext(Options()))
            {
                var service = Real(db, publisher);
                Assert.Equal(409, (await service.BuildAsync(period)).StatusCode);
                Assert.Equal(409, (await service.ConfirmAsync(seed.AdminId, batchId)).StatusCode);
            }

            await using var verify = new AppDbContext(Options());
            var batch = await verify.PayoutBatches.AsNoTracking().SingleAsync(b => b.BatchId == batchId);
            Assert.Equal("CLOSED", batch.BatchStatus);
            Assert.Equal(416000m, batch.TotalAmount);
            var item = await verify.PayoutItems.AsNoTracking().SingleAsync(i => i.BatchId == batchId);
            Assert.Equal("TRANSFERRED", item.ItemStatus);
            Assert.NotNull(item.TransferredAt);
            Assert.Equal(2, item.JobCount);

            var audit = Assert.Single(await verify.AdminAuditLogs.AsNoTracking()
                .Where(a => a.EntityType == "PAYOUT_BATCH" && a.EntityId == batchId.ToString()).ToListAsync());
            Assert.Equal("CLOSED", audit.NewValue);
            var published = Assert.Single(publisher.Events);
            Assert.Equal((batchId, period, 416000m, 1), (published.BatchId, published.PeriodMonth, published.TotalAmount, published.ItemCount));
        }
        finally
        {
            await CleanAsync(seed, period);
        }
    }

    [Fact]
    public async Task Real_database_a_penalty_larger_than_the_month_carries_to_the_next_batch_and_is_not_taken_twice()
    {
        if (!IsSqlServerAvailable()) return;

        var seed = await SeedBaseAsync();
        try
        {
            var f = await AddWorkerAsync(seed, "Freelancer F", "111");
            var feb = await AddAssignmentAsync(seed, f, Utc(2096, 2, 10, 3)); // payable 208000
            var mar = await AddAssignmentAsync(seed, f, Utc(2096, 3, 10, 3), gross: 500000m); // payable 400000
            var apr = await AddAssignmentAsync(seed, f, Utc(2096, 4, 10, 3)); // payable 208000

            // A freelancer-fault verdict of 600000 resolved in February; and two verdicts that must NOT count.
            var febOrder = await OrderOfAsync(feb);
            await AddResolvedDisputeAsync(febOrder, FaultParty.Freelancer, 600000m, Utc(2096, 2, 20, 3));
            await AddResolvedDisputeAsync(await OrderOfAsync(mar), FaultParty.Customer, 999999m, Utc(2096, 3, 20, 3)); // customer at fault
            await AddResolvedDisputeAsync(await OrderOfAsync(apr), FaultParty.Agency, 999999m, Utc(2096, 4, 20, 3)); // agency fault, not an absence fee

            int febBatch, marBatch;
            await using (var db = new AppDbContext(Options()))
            {
                var service = Real(db);
                var built = await service.BuildAsync("2096-02");
                febBatch = built.Data!.BatchId;
                var item = Assert.Single((await service.GetAsync(febBatch, null, null, null)).Data!.Items);
                Assert.Equal(208000m, item.PenaltyAmount); // capped by what the worker earns: 260000 - 52000
                Assert.Equal(0m, item.NetAmount);
                Assert.True((await service.ConfirmAsync(seed.AdminId, febBatch)).Success);
            }

            await using (var db = new AppDbContext(Options()))
            {
                var service = Real(db);
                marBatch = (await service.BuildAsync("2096-03")).Data!.BatchId;
                var item = Assert.Single((await service.GetAsync(marBatch, null, null, null)).Data!.Items);
                Assert.Equal(392000m, item.PenaltyAmount); // 600000 - 208000 still pending, less than the 400000 payable
                Assert.Equal(8000m, item.NetAmount); // 500000 - 100000 - 392000
                Assert.True((await service.ConfirmAsync(seed.AdminId, marBatch)).Success);
            }

            await using (var db = new AppDbContext(Options()))
            {
                var service = Real(db);
                var item = Assert.Single((await service.GetAsync((await service.BuildAsync("2096-04")).Data!.BatchId, null, null, null)).Data!.Items);
                Assert.Equal(0m, item.PenaltyAmount); // fully taken in the two earlier batches
                Assert.Equal(208000m, item.NetAmount);
            }
        }
        finally
        {
            await CleanAsync(seed, "2096-02", "2096-03", "2096-04");
        }
    }

    [Fact]
    public async Task Real_database_a_reversed_absence_fee_is_deducted_from_the_agency_only_when_the_fault_is_the_agencys()
    {
        if (!IsSqlServerAvailable()) return;

        var seed = await SeedBaseAsync(withAgency: true);
        const string period = "2095-07";
        try
        {
            var s = await AddWorkerAsync(seed, "Agency Staff S", "555", agencyStaff: true);
            var job = await AddAssignmentAsync(seed, s, Utc(2095, 7, 10, 3), gross: 300000m, rate: 0.150m, agencyId: seed.AgencyId);
            var absent = await AddAssignmentAsync(seed, s, Utc(2095, 7, 11, 3), agencyId: seed.AgencyId, absenceFee: 104000m);
            await AddResolvedDisputeAsync(await OrderOfAsync(absent), FaultParty.Agency, 104000m, Utc(2095, 7, 12, 3), category: "ABSENT_FEE");
            await AddResolvedDisputeAsync(await OrderOfAsync(job), FaultParty.Agency, 50000m, Utc(2095, 7, 12, 3)); // quality fault: escrow, not payout

            await using var db = new AppDbContext(Options());
            var service = Real(db);
            var batchId = (await service.BuildAsync(period)).Data!.BatchId;
            var item = Assert.Single((await service.GetAsync(batchId, null, null, null)).Data!.Items);

            Assert.Equal("AGENCY", item.PayeeType);
            Assert.Equal(104000m, item.PenaltyAmount); // only the absence-fee reversal
            Assert.Equal(300000m + 104000m, item.GrossAmount);
            Assert.Equal(45000m, item.CommissionAmount);
            Assert.Equal(300000m + 104000m - 45000m - 104000m, item.NetAmount);
        }
        finally
        {
            await CleanAsync(seed, period);
        }
    }

    [Fact]
    public async Task Real_database_four_simultaneous_builds_of_one_month_leave_one_batch_with_the_right_numbers()
    {
        if (!IsSqlServerAvailable()) return;

        var seed = await SeedBaseAsync();
        const string period = "2094-09";
        try
        {
            var f = await AddWorkerAsync(seed, "Freelancer F", "111");
            var g = await AddWorkerAsync(seed, "Freelancer G", "222");
            await AddAssignmentAsync(seed, f, Utc(2094, 9, 10, 3));
            await AddAssignmentAsync(seed, f, Utc(2094, 9, 11, 3));
            await AddAssignmentAsync(seed, g, Utc(2094, 9, 12, 3));

            using var gate = new ManualResetEventSlim(false);

            async Task<int> Build()
            {
                await using var db = new AppDbContext(Options());
                var service = Real(db);
                gate.Wait();
                return (await service.BuildAsync(period)).StatusCode;
            }

            var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(Build)).ToArray();
            await Task.Delay(300);
            gate.Set();
            var codes = await Task.WhenAll(tasks);

            Assert.Equal(1, codes.Count(c => c == 201));
            Assert.Equal(3, codes.Count(c => c == 200)); // the others waited for the lock and rebuilt the same DRAFT
            await using var verify = new AppDbContext(Options());
            var batch = await verify.PayoutBatches.AsNoTracking().SingleAsync(b => b.PeriodMonth == period);
            Assert.Equal(3 * 208000m, batch.TotalAmount);
            var items = await verify.PayoutItems.AsNoTracking().Where(i => i.BatchId == batch.BatchId).ToListAsync();
            Assert.Equal(2, items.Count); // one item per payee, no duplicates
            Assert.Equal(2 * 208000m, items.Single(i => i.WorkerId == f).NetAmount);
            Assert.Equal(3, await verify.JobAssignments.CountAsync(a => seed.AssignmentIds.Contains(a.AssignmentId) && a.PayoutItemId != null));
        }
        finally
        {
            await CleanAsync(seed, period);
        }
    }

    [Fact]
    public async Task Real_database_the_list_is_newest_month_first_with_item_counts()
    {
        if (!IsSqlServerAvailable()) return;

        var seed = await SeedBaseAsync();
        try
        {
            var f = await AddWorkerAsync(seed, "Freelancer F", "111");
            await AddAssignmentAsync(seed, f, Utc(2093, 3, 10, 3));
            await AddAssignmentAsync(seed, f, Utc(2093, 4, 10, 3));

            await using var db = new AppDbContext(Options());
            var service = Real(db);
            await service.BuildAsync("2093-03");
            await service.BuildAsync("2093-04");

            var page = (await service.ListAsync("1", "100")).Data!;
            var mine = page.Items.Where(b => b.PeriodMonth.StartsWith("2093-")).ToList();
            Assert.Equal(["2093-04", "2093-03"], mine.Select(b => b.PeriodMonth).ToArray());
            Assert.All(mine, b => Assert.Equal(1, b.ItemCount));
            Assert.All(mine, b => Assert.Equal(208000m, b.TotalAmount));
        }
        finally
        {
            await CleanAsync(seed, "2093-03", "2093-04");
        }
    }

    private static async Task<long> OrderOfAsync(long assignmentId)
    {
        await using var db = new AppDbContext(Options());
        return await db.JobAssignments.AsNoTracking().Where(a => a.AssignmentId == assignmentId).Select(a => a.OrderId).SingleAsync();
    }
}
