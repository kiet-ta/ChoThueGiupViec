using CommonService.Application.Common.Options;
using CommonService.Application.Features.Admin.Services;
using CommonService.Application.Features.Disputes.Dtos;
using CommonService.Application.Features.Disputes.Services;
using CommonService.Application.Features.Payouts.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modules.Admin;
using CommonService.Infrastructure.Modules.Disputes;
using CommonService.Infrastructure.Modules.Payouts;
using CommonService.Infrastructure.Persistence;
using CommonService.Tests.Payouts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Tests.Admin;

/// <summary>
/// BE-M6-08: the M6 money chain with the real services on the local SQL Server. Each module is tested on its own in its ticket; this
/// test checks that they agree with each other: an approved absence fee (BE-M6-03) is paid with no commission, a dispute verdict
/// (BE-M6-02) becomes a payout deduction, and the monthly batch (BE-M6-04) is exact, aggregates an agency, and can be run again without
/// counting anything twice. The month (2088-03) is far in the future so no real data is touched; every seeded row is removed afterwards.
/// </summary>
public class M6AcceptanceTests
{
    private sealed class SilentPublisher : IPublisher
    {
        public List<object> Published { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            Published.Add(notification);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            Publish((object)notification, cancellationToken);
    }

    private static PayoutBatchServiceTests.TestClock ClockAt(int y, int m, int d, int h = 5) =>
        new(new DateTime(y, m, d, h, 0, 0, DateTimeKind.Utc));

    /// <summary>A CHECKED_IN assignment whose worker reported the customer absent 5 minutes before <paramref name="reportedAtUtc"/> (GPS verified, 2 calls, there for 30 minutes).</summary>
    private static async Task<long> AddAbsentReportAsync(PayoutBatchDatabaseTests.Seed seed, int workerId, DateTime reportedAtUtc)
    {
        await using var db = new AppDbContext(PayoutBatchDatabaseTests.Options());

        var order = new JobOrder
        {
            OrderCode = "ORD" + Random.Shared.Next(100000, 999999),
            CustomerId = seed.CustomerId,
            AddressId = seed.AddressId,
            ServiceTier = ServiceTier.Economy,
            ScheduledDate = new DateOnly(2085, 1, 1).AddDays(Random.Shared.Next(1, 3000)),
            ShiftCode = "SHIFT1",
            AreaSnapshotM2 = 80m,
            RequiredWorkers = 1,
            TotalAmount = 260000m,
            CreatedAt = reportedAtUtc,
            UpdatedAt = reportedAtUtc,
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
            UpdatedAt = reportedAtUtc,
        };
        db.BookingSlots.Add(slot);
        await db.SaveChangesAsync();

        var assignment = new JobAssignment
        {
            OrderId = order.OrderId,
            CustomerId = seed.CustomerId,
            WorkerId = workerId,
            SlotId = slot.SlotId,
            ServiceTier = ServiceTier.Economy,
            AssignmentSeq = 1,
            DispatchRadiusKm = 5,
            GrossAmount = 260000m,
            CommissionRate = 0.200m,
            PayoutAmount = 208000m,
            CreatedAt = reportedAtUtc,
            UpdatedAt = reportedAtUtc,
        };
        assignment.TransitionTo(JobAssignmentStatus.Assigned);
        assignment.TransitionTo(JobAssignmentStatus.CheckedIn);
        db.JobAssignments.Add(assignment);
        await db.SaveChangesAsync();

        db.CheckInLogs.Add(new CheckInLog
        {
            AssignmentId = assignment.AssignmentId,
            DeviceLat = 10.76m,
            DeviceLng = 106.66m,
            DistanceM = 12.5m,
            GpsVerified = true,
            CallAttempts = 2,
            CustomerAbsentAt = reportedAtUtc.AddMinutes(-5),
            CheckedInAt = reportedAtUtc.AddMinutes(-30),
        });
        await db.SaveChangesAsync();

        seed.OrderIds.Add(order.OrderId);
        seed.SlotIds.Add(slot.SlotId);
        seed.AssignmentIds.Add(assignment.AssignmentId);
        return assignment.AssignmentId;
    }

    private static async Task<int> AddInReviewDisputeAsync(long orderId, DateTime createdAtUtc)
    {
        await using var db = new AppDbContext(PayoutBatchDatabaseTests.Options());
        var ticket = new DisputeTicket
        {
            OrderId = orderId,
            RaisedBy = "CUSTOMER",
            Category = "QUALITY",
            Description = "The kitchen is still dirty",
            EvidenceUrls = "[\"/uploads/a.jpg\"]",
            DisputeStatus = "IN_REVIEW",
            SlaDueAt = createdAtUtc.AddHours(48),
            CreatedAt = createdAtUtc,
        };
        db.DisputeTickets.Add(ticket);
        await db.SaveChangesAsync();
        return ticket.DisputeId;
    }

    [Fact]
    public async Task Real_database_the_money_chain_absence_fee_dispute_penalty_and_the_monthly_batch_agree_and_a_rerun_counts_nothing_twice()
    {
        if (!PayoutBatchDatabaseTests.IsSqlServerAvailable()) return;

        var seed = await PayoutBatchDatabaseTests.SeedBaseAsync(withAgency: true);
        const string period = "2088-03";
        var absenceClock = ClockAt(2088, 3, 10); // the Admin approves the absence on 10 March
        var disputeClock = ClockAt(2088, 3, 20); // and decides the dispute on 20 March
        var payoutClock = ClockAt(2088, 4, 5); // the batch is built in April, when March is over
        var refunds = new FakeRefundService();
        var publisher = new SilentPublisher();
        long? absenceAssignment = null;
        int? disputeId = null;
        try
        {
            var freelancer = await PayoutBatchDatabaseTests.AddWorkerAsync(seed, "Freelancer One", "111222");
            var second = await PayoutBatchDatabaseTests.AddWorkerAsync(seed, "Freelancer Two", "333444");
            var staff = await PayoutBatchDatabaseTests.AddWorkerAsync(seed, "Agency Staff", "555", agencyStaff: true);

            await PayoutBatchDatabaseTests.AddAssignmentAsync(seed, freelancer, PayoutBatchDatabaseTests.Utc(2088, 3, 5, 3)); // job 1: 260000
            var disputedJob = await PayoutBatchDatabaseTests.AddAssignmentAsync(seed, freelancer, PayoutBatchDatabaseTests.Utc(2088, 3, 8, 3)); // job 2: 260000, later disputed
            await PayoutBatchDatabaseTests.AddAssignmentAsync(seed, second, PayoutBatchDatabaseTests.Utc(2088, 3, 6, 3), gross: 100001m); // 80 % of 100001 with rounding
            await PayoutBatchDatabaseTests.AddAssignmentAsync(seed, staff, PayoutBatchDatabaseTests.Utc(2088, 3, 7, 3), gross: 300000m, rate: 0.150m, agencyId: seed.AgencyId);
            await PayoutBatchDatabaseTests.AddAssignmentAsync(seed, staff, PayoutBatchDatabaseTests.Utc(2088, 3, 9, 3), gross: 300000m, rate: 0.150m, agencyId: seed.AgencyId);
            absenceAssignment = await AddAbsentReportAsync(seed, freelancer, absenceClock.UtcNow);

            // ---- BE-M6-03: the Admin approves the customer absence: 40 % to the worker, 60 % back to the customer.
            await using (var db = new AppDbContext(PayoutBatchDatabaseTests.Options()))
            {
                var service = new AbsenceReportService(
                    new EfAbsenceRepository(db), refunds, new EfAuditLog(db, absenceClock), new UnitOfWork(db), absenceClock, publisher,
                    Microsoft.Extensions.Options.Options.Create(new BusinessRules()));

                var approved = await service.ApproveAsync(seed.AdminId, absenceAssignment.Value);

                Assert.True(approved.Success);
                Assert.Equal(104000m, approved.Data!.AbsenceFeeAmount); // exactly 40 % of 260000
                Assert.Equal(156000m, approved.Data.CustomerRefundAmount); // exactly 60 %
            }

            // ---- BE-M6-02: the Admin decides the dispute against the freelancer: the customer is compensated first.
            await using (var db = new AppDbContext(PayoutBatchDatabaseTests.Options()))
            {
                var order = await db.JobAssignments.AsNoTracking().Where(a => a.AssignmentId == disputedJob).Select(a => a.OrderId).SingleAsync();
                disputeId = await AddInReviewDisputeAsync(order, disputeClock.UtcNow.AddHours(-3));

                var verdict = new DisputeVerdictService(
                    new EfDisputeRepository(db), refunds, new FakeSlaPenaltyService(), new EfAuditLog(db, disputeClock), new UnitOfWork(db), disputeClock, publisher);

                var resolved = await verdict.ResolveAsync(seed.AdminId, disputeId.Value,
                    new ResolveDisputeRequestDto { FaultParty = "FREELANCER", CompensationAmount = 50000m, Note = "Photos show the kitchen was not cleaned" });

                Assert.True(resolved.Success);
                Assert.Equal("RESOLVED", resolved.Data!.DisputeStatus);
            }

            Assert.Equal([156000m, 50000m], refunds.Requests.Select(r => r.Amount).ToArray()); // the money that went back to the customers

            // ---- BE-M6-04: the monthly batch.
            PayoutBatchService Batch(AppDbContext db) =>
                new(new EfPayoutRepository(db), new EfPayoutPenaltySource(db), new EfAuditLog(db, payoutClock), new UnitOfWork(db), payoutClock, publisher);

            int batchId;
            await using (var db = new AppDbContext(PayoutBatchDatabaseTests.Options()))
            {
                var built = await Batch(db).BuildAsync(period);

                Assert.Equal(201, built.StatusCode);
                Assert.Equal(3, built.Data!.ItemCount);
                Assert.Equal(470000m + 80001m + 510000m, built.Data.TotalAmount);
                batchId = built.Data.BatchId;
            }

            async Task AssertItemsAsync()
            {
                await using var db = new AppDbContext(PayoutBatchDatabaseTests.Options());
                var items = (await Batch(db).GetAsync(batchId, null, null, null)).Data!.Items;
                Assert.Equal(["Freelancer One", "Freelancer Two", "Payout Agency Co"], items.Select(i => i.PayeeName).ToArray());

                var one = items[0];
                Assert.Equal(3, one.JobCount); // two completed jobs and the absence fee
                Assert.Equal(624000m, one.GrossAmount); // 260000 + 260000 + 104000
                Assert.Equal(104000m, one.CommissionAmount); // 20 % of the two jobs only: the absence fee carries none
                Assert.Equal(50000m, one.PenaltyAmount); // the dispute verdict became a deduction
                Assert.Equal(470000m, one.NetAmount);

                var two = items[1];
                Assert.Equal(100001m, two.GrossAmount);
                Assert.Equal(20000m, two.CommissionAmount); // 20000.2 rounded
                Assert.Equal(80001m, two.NetAmount); // the freelancer's 80 %

                var agency = items[2];
                Assert.Equal("AGENCY", agency.PayeeType);
                Assert.Equal(2, agency.JobCount); // one aggregated item for the agency, not one per run
                Assert.Equal(600000m, agency.GrossAmount);
                Assert.Equal(90000m, agency.CommissionAmount); // the agency's own 15 %
                Assert.Equal(510000m, agency.NetAmount);
            }

            await AssertItemsAsync();

            // Running it again changes nothing: the same batch, the same items, no assignment counted twice.
            await using (var db = new AppDbContext(PayoutBatchDatabaseTests.Options()))
            {
                var again = await Batch(db).BuildAsync(period);

                Assert.Equal(200, again.StatusCode);
                Assert.Equal(batchId, again.Data!.BatchId);
                Assert.Equal(470000m + 80001m + 510000m, again.Data.TotalAmount);
            }

            await AssertItemsAsync();
            await using (var db = new AppDbContext(PayoutBatchDatabaseTests.Options()))
            {
                Assert.Equal(1, await db.PayoutBatches.CountAsync(b => b.PeriodMonth == period));
                Assert.Equal(3, await db.PayoutItems.CountAsync(i => i.BatchId == batchId));
                var paid = await db.JobAssignments.AsNoTracking()
                    .CountAsync(a => seed.AssignmentIds.Contains(a.AssignmentId) && a.PayoutItemId != null);
                Assert.Equal(6, paid); // five jobs and the absence fee, each exactly once
            }

            // Confirming closes the month for good.
            await using (var db = new AppDbContext(PayoutBatchDatabaseTests.Options()))
            {
                var closed = await Batch(db).ConfirmAsync(seed.AdminId, batchId);
                Assert.True(closed.Success);
                Assert.Equal("CLOSED", closed.Data!.BatchStatus);
            }

            await using (var db = new AppDbContext(PayoutBatchDatabaseTests.Options()))
            {
                Assert.Equal(409, (await Batch(db).BuildAsync(period)).StatusCode);
            }

            await AssertItemsAsync();
        }
        finally
        {
            await using (var db = new AppDbContext(PayoutBatchDatabaseTests.Options()))
            {
                var ids = new List<string>();
                if (absenceAssignment is { } a) ids.Add(a.ToString());
                if (disputeId is { } d) ids.Add(d.ToString());
                await db.AdminAuditLogs.Where(l => (l.EntityType == "JOB_ASSIGNMENT" || l.EntityType == "DISPUTE_TICKET") && ids.Contains(l.EntityId)).ExecuteDeleteAsync();
                await db.CheckInLogs.Where(c => seed.AssignmentIds.Contains(c.AssignmentId)).ExecuteDeleteAsync();
            }

            await PayoutBatchDatabaseTests.CleanAsync(seed, period);
        }
    }
}
