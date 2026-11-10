using CommonService.Application.Features.Ledger;
using CommonService.Application.Features.Payouts;
using CommonService.Application.Features.Payouts.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Modules.Admin;
using CommonService.Infrastructure.Modules.Ledger;
using CommonService.Infrastructure.Modules.Payouts;
using CommonService.Infrastructure.Persistence;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Tests.Payouts;

/// <summary>
/// The monthly settlement against SQL Server: the real queries of the ledger and of the payout tables (decision D28). Runs only against
/// the scratch database <c>LedgerTicket725Test</c> (set <c>ConnectionStrings__DefaultConnection</c>), inside one transaction that is
/// rolled back; the per-payee units of work are replaced by a pass-through so they share that transaction.
/// </summary>
public class PayoutSettlementSqlTests
{
    private static readonly DateTime InOctober = new(2026, 10, 15, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 11, 10, 4, 0, 0, DateTimeKind.Utc);
    private const int Worker = 987701;
    private const int Agency = 987702;

    private class PassThroughUnitOfWork(AppDbContext db) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => db.SaveChangesAsync(cancellationToken);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default)
        {
            var result = await action();
            await db.SaveChangesAsync(cancellationToken);
            return result;
        }
    }

    /// <summary>The scratch database has no WORKER rows: the repository is the real one except that every worker is a freelancer.</summary>
    private class FreelancerEarnings(IWorkerEarningsRepository inner) : IWorkerEarningsRepository
    {
        public Task<WorkerType?> GetWorkerTypeAsync(int workerId, CancellationToken cancellationToken = default) => Task.FromResult<WorkerType?>(WorkerType.Freelancer);

        public Task<WorkerBatchState?> GetBatchStateAsync(int workerId, string periodMonth, CancellationToken cancellationToken = default) =>
            inner.GetBatchStateAsync(workerId, periodMonth, cancellationToken);

        public Task<(IReadOnlyList<WorkerPayoutHistoryRow> Items, int Total)> GetClosedItemsAsync(int workerId, int page, int pageSize, CancellationToken cancellationToken = default) =>
            inner.GetClosedItemsAsync(workerId, page, pageSize, cancellationToken);
    }

    private class RecordingPublisher : IPublisher
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

    private static NewLedgerEntry Entry(LedgerParty from, LedgerParty to, decimal amount, LedgerEntryType type, LedgerRefType refType, long refId) =>
        new(from, to, amount, type, refType, refId, null, null, "sql-settle:" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Sql_settlement_reads_the_ledger_stores_the_items_writes_the_entries_closes_by_the_system_and_the_files_show_advance_and_carry_in()
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrEmpty(connection) || new SqlConnectionStringBuilder(connection).InitialCatalog != "LedgerTicket725Test") return;
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        foreach (var name in new[] { "PAYOUT_ITEM", "JOB_ASSIGNMENT" }) await db.Database.ExecuteSqlRawAsync($"ALTER TABLE {name} NOCHECK CONSTRAINT ALL");

        var jobs = new[] { 1, 2 }.Select(slot => new JobAssignment
        {
            OrderId = 1,
            CustomerId = 12,
            WorkerId = Worker,
            SlotId = slot,
            ServiceTier = ServiceTier.Economy,
            AssignmentSeq = 1,
            GrossAmount = 260_000m,
            CommissionRate = 0.20m,
            PayoutAmount = 208_000m,
            CreatedAt = InOctober,
            UpdatedAt = InOctober,
        }).ToList();
        db.JobAssignments.AddRange(jobs);
        await db.SaveChangesAsync();

        // October, as the event handlers wrote it (the worked example) and the withdrawal flow left it.
        var october = new FakeClock();
        october.Set(InOctober);
        var events = new LedgerWriter(new EfLedgerRepository(db), october);
        var worker = new LedgerParty(LedgerPartyType.Worker, Worker);
        var agency = new LedgerParty(LedgerPartyType.Agency, Agency);
        var external = new LedgerParty(LedgerPartyType.External, null);
        await events.AppendAsync(
        [
            Entry(LedgerRules.Clearing, worker, 208_000m, LedgerEntryType.JobEarning, LedgerRefType.Assignment, jobs[0].AssignmentId),
            Entry(LedgerRules.Clearing, LedgerRules.Revenue, 52_000m, LedgerEntryType.Commission, LedgerRefType.Assignment, jobs[0].AssignmentId),
            Entry(LedgerRules.Clearing, worker, 80_000m, LedgerEntryType.ExtensionEarning, LedgerRefType.Extension, 5),
            Entry(LedgerRules.Clearing, LedgerRules.Revenue, 20_000m, LedgerEntryType.Commission, LedgerRefType.Extension, 5),
            Entry(worker, external, 100_000m, LedgerEntryType.Withdrawal, LedgerRefType.Withdrawal, 1),
            Entry(worker, LedgerRules.Revenue, 50_000m, LedgerEntryType.Penalty, LedgerRefType.Dispute, 4),
            Entry(LedgerRules.Clearing, agency, 221_000m, LedgerEntryType.JobEarning, LedgerRefType.Assignment, jobs[1].AssignmentId),
            Entry(LedgerRules.Clearing, LedgerRules.Revenue, 39_000m, LedgerEntryType.Commission, LedgerRefType.Assignment, jobs[1].AssignmentId),
        ]);

        var clock = new FakeClock();
        clock.Set(Now);
        var publisher = new RecordingPublisher();
        var repository = new EfPayoutRepository(db);
        var service = new PayoutBatchService(
            repository, new EfLedgerQuery(db), new LedgerWriter(new EfLedgerRepository(db), clock), new EfAuditLog(db, clock), new PassThroughUnitOfWork(db), clock,
            publisher, NullLogger<PayoutBatchService>.Instance);

        var settled = await service.SettleAsync("2026-10");

        Assert.Equal(201, settled.StatusCode);
        Assert.Equal(("CLOSED", null, 2, 138_000m + 260_000m - 39_000m), (settled.Data!.BatchStatus, settled.Data.ConfirmedBy, settled.Data.ItemCount, settled.Data.TotalAmount));

        var detail = (await service.GetAsync(settled.Data.BatchId, null, null, null)).Data!;
        var freelancer = detail.Items.Single(i => i.WorkerId == Worker);
        Assert.Equal((1, 360_000m, 72_000m, 50_000m, 100_000m, 0m, 138_000m, "TRANSFERRED"),
            (freelancer.JobCount, freelancer.GrossAmount, freelancer.CommissionAmount, freelancer.PenaltyAmount, freelancer.AdvanceAmount, freelancer.CarryInAmount, freelancer.NetAmount, freelancer.ItemStatus));
        var agencyItem = detail.Items.Single(i => i.AgencyId == Agency);
        Assert.Equal((1, 260_000m, 39_000m, 221_000m), (agencyItem.JobCount, agencyItem.GrossAmount, agencyItem.CommissionAmount, agencyItem.NetAmount));

        var settlements = await db.LedgerEntries.AsNoTracking().Where(e => e.EntryType == LedgerEntryType.Settlement).OrderBy(e => e.EntryId).ToListAsync();
        Assert.Equal(["SETTLEMENT:2026-10:WORKER:" + Worker, "SETTLEMENT:2026-10:AGENCY:" + Agency], settlements.Select(s => s.IdempotencyKey).ToArray());
        Assert.Equal([138_000m, 221_000m], settlements.Select(s => s.Amount).ToArray());
        Assert.Equal([LedgerRefType.PayoutItem, LedgerRefType.PayoutItem], settlements.Select(s => s.RefType).ToArray());
        Assert.Equal(freelancer.ItemId, (int)settlements[0].RefId);
        Assert.Equal(freelancer.ItemId, (await db.JobAssignments.AsNoTracking().SingleAsync(a => a.AssignmentId == jobs[0].AssignmentId)).PayoutItemId);
        Assert.Equal(agencyItem.ItemId, (await db.JobAssignments.AsNoTracking().SingleAsync(a => a.AssignmentId == jobs[1].AssignmentId)).PayoutItemId);

        var batch = await db.PayoutBatches.AsNoTracking().SingleAsync(b => b.BatchId == settled.Data.BatchId);
        Assert.Equal((PayoutConstants.BatchClosed, (int?)null, Now), (batch.BatchStatus, batch.ConfirmedBy, batch.ConfirmedAt));
        var audit = await db.AdminAuditLogs.AsNoTracking().SingleAsync(a => a.EntityType == "PAYOUT_BATCH" && a.EntityId == batch.BatchId.ToString());
        Assert.Equal((AuditActorType.System, (int?)null), (audit.ActorType, audit.AdminId));
        Assert.Equal(settled.Data.BatchId, Assert.Single(publisher.Events).BatchId);
        Assert.True((await new LedgerChainVerifier(new EfLedgerRepository(db)).VerifyAsync()).Ok);

        // The balance of both payees is zero and a second run does nothing at all.
        var balance = new LedgerBalanceService(new EfLedgerQuery(db), new FakeOpenDisputeQuery(), clock);
        Assert.Equal(0m, (await balance.GetAsync(worker)).Balance);
        Assert.Equal(0m, (await balance.GetAsync(agency)).Balance);
        var again = await service.SettleAsync("2026-10");
        Assert.Equal((200, settled.Data.BatchId), (again.StatusCode, again.Data!.BatchId));
        Assert.Equal(2, await db.LedgerEntries.CountAsync(e => e.EntryType == LedgerEntryType.Settlement));
        Assert.Single(publisher.Events);

        // The worker's income screen reads the same month: the closed item is the answer, with the advance and the carry-in.
        var earningsRepository = new EfWorkerEarningsRepository(db);
        var state = (await earningsRepository.GetBatchStateAsync(Worker, "2026-10"))!;
        Assert.Equal(("CLOSED", 1, 360_000m, 72_000m, 50_000m, 100_000m, 0m, 138_000m),
            (state.BatchStatus, state.Item!.JobCount, state.Item.GrossAmount, state.Item.CommissionAmount, state.Item.PenaltyAmount, state.Item.AdvanceAmount, state.Item.CarryInAmount, state.Item.NetAmount));
        var (history, total) = await earningsRepository.GetClosedItemsAsync(Worker, 1, 10);
        Assert.Equal((1, "2026-10", 100_000m, 0m, 138_000m), (total, history.Single().PeriodMonth, history.Single().AdvanceAmount, history.Single().CarryInAmount, history.Single().NetAmount));
        var screen = (await new WorkerEarningsService(new FreelancerEarnings(earningsRepository), new EfLedgerQuery(db), clock).GetEarningsAsync(Worker, "2026-10")).Data!;
        Assert.Equal(("TRANSFERRED", 100_000m, 138_000m), (screen.PayoutStatus, screen.AdvanceAmount, screen.NetAmount));
        Assert.Equal([jobs[0].AssignmentId], screen.Jobs.Select(j => j.AssignmentId).ToArray());

        // The transfer file of the freelancers carries the advance and the carry-in columns.
        var export = new PayoutExportService(repository, new FakeFileStorage(), clock);
        var file = (await export.ExportAsync(settled.Data.BatchId, "freelancer")).Data!;
        var table = XlsxTestReader.Table(file.Content, 7);
        Assert.Equal(["full_name", "bank_name", "bank_account_no", "advance_amount", "carry_in_amount", "net_amount", "transfer_note"], table[0]);
        Assert.Contains(table.Skip(1), row => row[3] == "100000" && row[4] == "0" && row[5] == "138000");
        await transaction.RollbackAsync();
    }
}
