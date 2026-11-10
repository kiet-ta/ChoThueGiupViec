using CommonService.Application.Features.Booking;
using CommonService.Application.Features.Ledger;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modules.Ledger;
using CommonService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Tests.Ledger;

/// <summary>
/// The SQL side of LEDGER-05: the read-only source query and the whole money flow through the real repository. Runs only against the
/// scratch database named <c>LedgerTicket725Test</c> (set <c>ConnectionStrings__DefaultConnection</c>); everything is rolled back.
/// </summary>
public class LedgerSourceQueryTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 4, 0, 0, DateTimeKind.Utc);
    private static readonly string[] Tables = ["JOB_ORDER", "JOB_ASSIGNMENT", "JOB_ORDER_EXTENSION", "PAYMENT_TRANSACTION", "DISPUTE_TICKET", "ESCROW_TRANSACTION"];

    [Fact]
    public async Task Sql_source_facts_feed_the_whole_money_flow_and_the_clearing_account_returns_to_zero()
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrEmpty(connection) || new SqlConnectionStringBuilder(connection).InitialCatalog != "LedgerTicket725Test") return;
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        foreach (var table in Tables) await db.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} NOCHECK CONSTRAINT ALL");

        // The source rows carry made-up foreign keys: the constraints above are off, inside this rolled-back transaction only.
        var order = new JobOrder
        {
            OrderCode = "LG" + Guid.NewGuid().ToString("N")[..10],
            CustomerId = 12,
            AddressId = 1,
            ServiceTier = ServiceTier.Economy,
            ScheduledDate = DateOnly.FromDateTime(Now),
            ShiftCode = "SHIFT1",
            AreaSnapshotM2 = 80m,
            RequiredWorkers = 1,
            TotalAmount = 260_000m,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        var other = new JobOrder
        {
            OrderCode = "LG" + Guid.NewGuid().ToString("N")[..10],
            CustomerId = 12,
            AddressId = 1,
            ServiceTier = ServiceTier.Economy,
            ScheduledDate = DateOnly.FromDateTime(Now),
            ShiftCode = "SHIFT1",
            AreaSnapshotM2 = 80m,
            RequiredWorkers = 1,
            TotalAmount = 100_000m,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        db.JobOrders.AddRange(order, other);
        await db.SaveChangesAsync();

        var assignment = new JobAssignment
        {
            OrderId = order.OrderId,
            CustomerId = 12,
            WorkerId = 7,
            SlotId = 1,
            ServiceTier = ServiceTier.Economy,
            AssignmentSeq = 1,
            GrossAmount = 260_000m,
            CommissionRate = 0.20m,
            PayoutAmount = 208_000m,
            CompletedAt = Now,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        db.JobAssignments.Add(assignment);
        db.Entry(assignment).Property(a => a.AssignmentStatus).CurrentValue = JobAssignmentStatus.Completed;
        var extension = new JobOrderExtension
        {
            OrderId = order.OrderId,
            WorkerId = 7,
            ExtraHours = 2m,
            ExtraAmount = 100_000m,
            WorkerDecision = WorkerDecisions.Accepted,
            ExtStatus = ExtensionStatuses.Accepted,
            RequestedAt = Now,
            CreatedAt = Now,
        };
        db.JobOrderExtensions.Add(extension);
        var unpaid = new JobOrderExtension
        {
            OrderId = other.OrderId,
            WorkerId = 7,
            ExtraHours = 1m,
            ExtraAmount = 50_000m,
            WorkerDecision = WorkerDecisions.Pending,
            ExtStatus = ExtensionStatuses.PendingPayment,
            RequestedAt = Now,
            CreatedAt = Now,
        };
        db.JobOrderExtensions.Add(unpaid);
        await db.SaveChangesAsync();

        db.PaymentTransactions.AddRange(
            new PaymentTransaction { GatewayTxnRef = "LG" + Guid.NewGuid().ToString("N"), OrderId = order.OrderId, Purpose = PaymentPurpose.Order, Gateway = "SEPAY", Amount = 260_000m, TxnStatus = PaymentStatus.Success, CreatedAt = Now },
            new PaymentTransaction { GatewayTxnRef = "LG" + Guid.NewGuid().ToString("N"), ExtensionId = extension.ExtensionId, Purpose = PaymentPurpose.Extension, Gateway = "SEPAY", Amount = 100_000m, TxnStatus = PaymentStatus.Refunded, RefundedAmount = 100_000m, RefundedAt = Now.AddHours(2), CreatedAt = Now },
            new PaymentTransaction { GatewayTxnRef = "LG" + Guid.NewGuid().ToString("N"), ExtensionId = unpaid.ExtensionId, Purpose = PaymentPurpose.Extension, Gateway = "SEPAY", Amount = 50_000m, TxnStatus = PaymentStatus.Pending, CreatedAt = Now });
        var ticket = new DisputeTicket
        {
            OrderId = order.OrderId,
            RaisedBy = "CUSTOMER",
            Category = "QUALITY",
            Description = "x",
            DisputeStatus = "RESOLVED",
            FaultParty = FaultParty.Freelancer,
            CompensationAmount = 50_000m,
            SlaDueAt = Now,
            ResolvedAt = Now,
            CreatedAt = Now,
        };
        db.DisputeTickets.Add(ticket);
        await db.SaveChangesAsync();

        var source = new EfLedgerSourceQuery(db);
        var readOrder = (await source.GetOrderAsync(order.OrderId))!;
        Assert.Equal((12, 260_000m, 1), (readOrder.CustomerId, readOrder.TotalAmount, readOrder.RequiredWorkers));
        var read = Assert.Single(await source.GetOrderAssignmentsAsync(order.OrderId));
        Assert.Equal((7, (int?)null, 1, 260_000m, 0.20m, true), (read.WorkerId, read.AgencyId, read.Seat, read.GrossAmount, read.CommissionRate, read.IsCompleted));
        Assert.True((await source.GetExtensionAsync(extension.ExtensionId))!.IsPaid);
        Assert.False((await source.GetExtensionAsync(unpaid.ExtensionId))!.IsPaid);
        Assert.Equal([extension.ExtensionId], (await source.GetOrderExtensionsAsync(order.OrderId)).Select(e => e.ExtensionId).ToArray());
        Assert.Equal([260_000m, 100_000m], (await source.GetOrderPaymentsAsync(order.OrderId)).Select(p => p.Amount).ToArray());
        var dispute = (await source.GetDisputeAsync(ticket.DisputeId))!;
        Assert.Equal((true, FaultParty.Freelancer, "QUALITY", 50_000m), (dispute.IsResolved, dispute.FaultParty, dispute.Category, dispute.CompensationAmount));
        Assert.Null(await source.GetOrderAsync(-1));

        var clock = new FakeClock();
        clock.Set(Now);
        var repository = new EfLedgerRepository(db);
        var posting = new LedgerPostingService(new LedgerWriter(repository, clock), source);
        await posting.PostOrderPaidAsync(new OrderPaid(order.OrderId, 12, 260_000m, "SHIFT1", Now.Date, 1));
        await posting.PostExtensionPaidAsync(new ExtensionPaid(extension.ExtensionId, order.OrderId, 7, 2m, 100_000m, Now));
        await posting.PostJobCompletedAsync(new JobCompleted(assignment.AssignmentId, order.OrderId, 7, null, 208_000m, Now));
        await posting.PostPenaltyAsync(new DisputeResolved(ticket.DisputeId, assignment.AssignmentId, FaultParty.Freelancer, 50_000m, "n", Now));
        Assert.Equal(0m, await repository.GetOrderClearingBalanceAsync(order.OrderId));

        // The whole 100,000 extension is refunded while the clearing account is empty: it comes from the revenue; replay adds nothing.
        var refunded = new OrderRefunded(order.OrderId, 12, 100_000m, "x", Now.AddHours(2));
        await posting.PostRefundAsync(refunded);
        await posting.PostRefundAsync(refunded);
        var rows = await db.LedgerEntries.AsNoTracking().Where(e => e.OrderId == order.OrderId).OrderBy(e => e.EntryId).ToListAsync();
        Assert.Equal(
            // The assignment is already completed when the extension is paid, so its shares are booked with the payment.
            [LedgerEntryType.OrderPaid, LedgerEntryType.ExtensionPaid, LedgerEntryType.ExtensionEarning, LedgerEntryType.Commission,
                LedgerEntryType.JobEarning, LedgerEntryType.Commission, LedgerEntryType.Penalty, LedgerEntryType.Refund],
            rows.Select(r => r.EntryType).ToArray());
        Assert.Equal(100_000m, await repository.GetOrderRefundTotalAsync(order.OrderId));
        Assert.True((await new LedgerChainVerifier(repository).VerifyAsync()).Ok);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Sql_sweep_reads_every_source_writes_the_missing_entries_and_rewrites_a_deleted_one()
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrEmpty(connection) || new SqlConnectionStringBuilder(connection).InitialCatalog != "LedgerTicket725Test") return;
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        foreach (var table in Tables) await db.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} NOCHECK CONSTRAINT ALL");

        var order = new JobOrder
        {
            OrderCode = "LG" + Guid.NewGuid().ToString("N")[..10],
            CustomerId = 12,
            AddressId = 1,
            ServiceTier = ServiceTier.Economy,
            ScheduledDate = DateOnly.FromDateTime(Now),
            ShiftCode = "SHIFT1",
            AreaSnapshotM2 = 80m,
            RequiredWorkers = 1,
            TotalAmount = 260_000m,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        db.JobOrders.Add(order);
        await db.SaveChangesAsync();
        var done = new JobAssignment
        {
            OrderId = order.OrderId,
            CustomerId = 12,
            WorkerId = 7,
            SlotId = 1,
            ServiceTier = ServiceTier.Economy,
            AssignmentSeq = 1,
            GrossAmount = 260_000m,
            CommissionRate = 0.20m,
            PayoutAmount = 208_000m,
            CompletedAt = Now.AddHours(-1),
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        var absent = new JobAssignment
        {
            OrderId = order.OrderId,
            CustomerId = 12,
            WorkerId = 8,
            SlotId = 2,
            ServiceTier = ServiceTier.Economy,
            AssignmentSeq = 2,
            GrossAmount = 260_000m,
            CommissionRate = 0.20m,
            PayoutAmount = 208_000m,
            AbsenceFeeAmount = 104_000m,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        db.JobAssignments.AddRange(done, absent);
        db.Entry(done).Property(a => a.AssignmentStatus).CurrentValue = JobAssignmentStatus.Completed;
        db.Entry(absent).Property(a => a.AssignmentStatus).CurrentValue = JobAssignmentStatus.Absent;
        db.PaymentTransactions.AddRange(
            new PaymentTransaction
            {
                GatewayTxnRef = "LG" + Guid.NewGuid().ToString("N"),
                OrderId = order.OrderId,
                Purpose = PaymentPurpose.Order,
                Gateway = "SEPAY",
                Amount = 260_000m,
                TxnStatus = PaymentStatus.Success,
                PaidAt = Now.AddHours(-2),
                RefundedAmount = 100_000m,
                RefundedAt = Now.AddHours(-1),
                CreatedAt = Now.AddHours(-2),
            },
            new PaymentTransaction
            {
                GatewayTxnRef = "LG" + Guid.NewGuid().ToString("N"),
                AgencyId = 3,
                Purpose = PaymentPurpose.Subscription,
                Gateway = "SEPAY",
                Amount = 500_000m,
                TxnStatus = PaymentStatus.Success,
                PaidAt = Now.AddHours(-2),
                CreatedAt = Now.AddHours(-2),
            });
        db.EscrowTransactions.AddRange(
            new EscrowTransaction { AgencyId = 3, TxnType = EscrowTransactionType.Deposit, Amount = 2_000_000m, BalanceAfter = 2_000_000m, Reason = "x", CreatedAt = Now },
            new EscrowTransaction { AgencyId = 3, TxnType = EscrowTransactionType.Penalty, Amount = 300_000m, BalanceAfter = 1_700_000m, Reason = "x", CreatedAt = Now });
        db.DisputeTickets.Add(new DisputeTicket
        {
            OrderId = order.OrderId,
            RaisedBy = "CUSTOMER",
            Category = "QUALITY",
            Description = "x",
            DisputeStatus = "RESOLVED",
            FaultParty = FaultParty.Freelancer,
            CompensationAmount = 50_000m,
            SlaDueAt = Now,
            ResolvedAt = Now,
            CreatedAt = Now,
        });
        await db.SaveChangesAsync();

        var clock = new FakeClock();
        clock.Set(Now);
        var repository = new EfLedgerRepository(db);
        var source = new EfLedgerSourceQuery(db);
        var writer = new LedgerWriter(repository, clock);
        var service = new LedgerReconciliationService(
            source, new EfLedgerQuery(db), repository, writer, new LedgerPostingService(writer, source), clock,
            Microsoft.Extensions.Options.Options.Create(new LedgerOptions { StartUtc = Now.AddDays(-1) }), NullLogger<LedgerReconciliationService>.Instance);

        var refunded = Assert.Single(await source.GetRefundedOrdersAsync(Now.AddDays(-1), Now.AddMinutes(-10)));
        Assert.Equal((order.OrderId, 12, 100_000m), (refunded.OrderId, refunded.CustomerId, refunded.RefundedTotal));
        Assert.Empty(await source.GetRefundedOrdersAsync(Now.AddDays(-1), Now.AddHours(-2))); // not quiet enough
        Assert.Empty((await new LedgerReconciliationService(
            source, new EfLedgerQuery(db), repository, writer, new LedgerPostingService(writer, source), clock,
            Microsoft.Extensions.Options.Options.Create(new LedgerOptions { StartUtc = Now.AddDays(1) }), NullLogger<LedgerReconciliationService>.Instance).ReportAsync()).Items);

        var report = await service.ReportAsync();
        Assert.Equal(
            ["ORDER_PAID", "JOB_EARNING", "COMMISSION", "REFUND", "ABSENCE_FEE", "PENALTY", "SUBSCRIPTION", "ESCROW_DEPOSIT", "ESCROW_PENALTY"],
            report.Items.Select(i => i.EntryType).ToArray());
        Assert.Equal(0, await db.LedgerEntries.CountAsync());

        var swept = await service.SweepAsync();
        Assert.Equal(9, swept.Written); // the dispute blames both freelancer assignments of the order: two penalties
        Assert.Equal(1, swept.RefundOrders);
        Assert.Equal(10, await db.LedgerEntries.CountAsync());
        Assert.Equal(100_000m, await repository.GetOrderRefundTotalAsync(order.OrderId));
        Assert.Empty((await service.ReportAsync()).Items);
        Assert.Equal((0, 0, 0), (await service.SweepAsync()) is var again ? (again.Missing, again.Written, again.RefundOrders) : default);

        await db.Database.ExecuteSqlRawAsync("DELETE FROM LEDGER_ENTRY WHERE idempotency_key LIKE 'COMMISSION:ASSIGNMENT:%'");
        var lost = Assert.Single((await service.ReportAsync()).Items);
        Assert.Equal(("COMMISSION", 1, 52_000m), (lost.EntryType, lost.MissingCount, lost.MissingAmount));
        await service.SweepAsync();
        Assert.Empty((await service.ReportAsync()).Items);
        Assert.Equal(10, await db.LedgerEntries.CountAsync());
        await transaction.RollbackAsync();
    }
}
