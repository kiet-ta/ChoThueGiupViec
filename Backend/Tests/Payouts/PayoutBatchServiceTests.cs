using System.Reflection;
using CommonService.Application.Common.Models;
using CommonService.Application.Features.Ledger;
using CommonService.Application.Features.Payouts;
using CommonService.Application.Features.Payouts.Dtos;
using CommonService.Application.Features.Payouts.Services;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using CommonService.Tests.Ledger;
using CommonService.WebAPI.Controllers.Payouts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Tests.Payouts;

/// <summary>
/// The monthly settlement from the ledger and the read side of the payout batches (decision D28, contract payouts.md and ledger.md
/// section 7), with in-memory repositories. The month is October 2026, the example of the handoff appendix: a 260,000 job and a
/// 100,000 extension at 20 % earn 288,000 (commission 72,000); 100,000 was withdrawn and 50,000 was deducted: net 138,000.
/// </summary>
public class PayoutBatchServiceTests
{
    // 2026-11-10 11:00 Asia/Ho_Chi_Minh: October 2026 is finished, November is not.
    private static readonly DateTime Now = new(2026, 11, 10, 4, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime InOctober = new(2026, 10, 15, 3, 0, 0, DateTimeKind.Utc);

    internal class TestClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => local.AddHours(-7);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(utcNow));
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

    private class RollbackTrackingUnitOfWork : IUnitOfWork
    {
        public int Committed { get; private set; }
        public int RolledBack { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await action();
                Committed++;
                return result;
            }
            catch
            {
                RolledBack++;
                throw;
            }
        }
    }

    /// <summary>The payout tables in memory: batches, items in the order they were stored, the locks taken and the assignments attached.</summary>
    internal class MemoryPayouts : IPayoutRepository
    {
        public List<PayoutBatch> Batches { get; } = [];
        public Dictionary<int, List<NewPayoutItem>> Stored { get; } = [];
        public Dictionary<PayeeKey, string> Banks { get; } = [];
        public Dictionary<int, string> ItemStatus { get; } = [];
        public Dictionary<PayeeKey, (string Name, string? Bank)> Names { get; } = [];
        public List<PayoutDetailRow> Details { get; } = [];
        public List<PayeeKey> Locked { get; } = [];
        public List<(int ItemId, IReadOnlyList<long> AssignmentIds)> Attached { get; } = [];
        public bool LockWorks { get; set; } = true;
        public PayeeKey? FailLockOnce { get; set; }
        public (int PageSize, PayeeType? Type)? LastItems { get; private set; }
        private readonly List<(int ItemId, int BatchId, PayeeKey Payee)> _items = [];
        private int _next = 1;
        private int _nextItem = 1;

        /// <summary>A batch with ready items, as the settlement leaves it (the export and read tests start from this).</summary>
        public int SeedBatch(string period, IEnumerable<NewPayoutItem> items, string status = "CLOSED")
        {
            var batch = new PayoutBatch { BatchId = _next++, PeriodMonth = period, BatchStatus = status, CreatedAt = Now };
            Batches.Add(batch);
            Stored[batch.BatchId] = [];
            foreach (var item in items)
            {
                var key = new PayeeKey(item.PayeeType, item.PayeeType == PayeeType.Freelancer ? item.WorkerId!.Value : item.AgencyId!.Value);
                _items.Add((_nextItem++, batch.BatchId, key));
                Stored[batch.BatchId].Add(item);
                Banks[key] = item.BankAccountNo;
            }

            batch.TotalAmount = Stored[batch.BatchId].Sum(i => i.NetAmount);
            ItemStatus[batch.BatchId] = "TRANSFERRED";
            return batch.BatchId;
        }

        public Task<bool> TryLockPeriodAsync(string periodMonth, CancellationToken cancellationToken = default) => Task.FromResult(LockWorks);

        public Task LockPayeeAsync(PayeeKey payee, CancellationToken cancellationToken = default)
        {
            if (FailLockOnce == payee)
            {
                FailLockOnce = null;
                throw new TimeoutException("The wallet lock could not be taken in time.");
            }

            Locked.Add(payee);
            return Task.CompletedTask;
        }

        public Task<PayoutBatch?> FindByPeriodAsync(string periodMonth, CancellationToken cancellationToken = default) =>
            Task.FromResult(Batches.FirstOrDefault(b => b.PeriodMonth == periodMonth));

        public Task<PayoutBatch?> GetBatchAsync(int batchId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Batches.FirstOrDefault(b => b.BatchId == batchId));

        public Task<PayoutBatch> AddDraftAsync(string periodMonth, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            var batch = new PayoutBatch { BatchId = _next++, PeriodMonth = periodMonth, BatchStatus = "DRAFT", CreatedAt = nowUtc };
            Batches.Add(batch);
            return Task.FromResult(batch);
        }

        public Task<int?> FindItemIdAsync(int batchId, PayeeKey payee, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.Where(i => i.BatchId == batchId && i.Payee == payee).Select(i => (int?)i.ItemId).FirstOrDefault());

        public Task<IReadOnlyDictionary<PayeeKey, string>> GetBankAccountsAsync(IReadOnlyCollection<PayeeKey> payees, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<PayeeKey, string>>(Banks.Where(b => payees.Contains(b.Key)).ToDictionary(b => b.Key, b => b.Value));

        public Task<int> AddItemAsync(int batchId, PayeeKey payee, string bankAccountNo, DateTime transferredAtUtc, CancellationToken cancellationToken = default)
        {
            var id = _nextItem++;
            _items.Add((id, batchId, payee));
            if (!Stored.ContainsKey(batchId)) Stored[batchId] = [];
            Stored[batchId].Add(new NewPayoutItem(
                payee.Type, payee.Type == PayeeType.Freelancer ? payee.Id : null, payee.Type == PayeeType.Agency ? payee.Id : null,
                0, 0m, 0m, 0m, 0m, 0m, 0m, bankAccountNo, []));
            ItemStatus[batchId] = "TRANSFERRED";
            return Task.FromResult(id);
        }

        public Task CompleteItemAsync(int itemId, NewPayoutItem figures, CancellationToken cancellationToken = default)
        {
            var (_, batchId, _) = _items.Single(i => i.ItemId == itemId);
            var index = _items.Where(i => i.BatchId == batchId).ToList().FindIndex(i => i.ItemId == itemId);
            Stored[batchId][index] = figures;
            Attached.Add((itemId, figures.AssignmentIds));
            return Task.CompletedTask;
        }

        public Task<decimal> SumNetAsync(int batchId, CancellationToken cancellationToken = default) =>
            Task.FromResult((Stored.GetValueOrDefault(batchId) ?? []).Sum(i => i.NetAmount));

        public Task<(IReadOnlyList<PayoutBatch> Items, int Total)> ListBatchesAsync(int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var all = Batches.OrderByDescending(b => b.PeriodMonth).ToList();
            return Task.FromResult<(IReadOnlyList<PayoutBatch>, int)>((all.Skip((page - 1) * pageSize).Take(pageSize).ToList(), all.Count));
        }

        public Task<IReadOnlyDictionary<int, int>> CountItemsAsync(IReadOnlyCollection<int> batchIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<int, int>>(batchIds.Where(Stored.ContainsKey).ToDictionary(id => id, id => Stored[id].Count));

        private PayoutItem ToEntity(int batchId, int index, NewPayoutItem n) => new()
        {
            ItemId = index + 1,
            BatchId = batchId,
            WorkerId = n.WorkerId,
            AgencyId = n.AgencyId,
            PayeeType = n.PayeeType,
            JobCount = n.JobCount,
            GrossAmount = n.GrossAmount,
            CommissionAmount = n.CommissionAmount,
            PenaltyAmount = n.PenaltyAmount,
            AdvanceAmount = n.AdvanceAmount,
            CarryInAmount = n.CarryInAmount,
            NetAmount = n.NetAmount,
            BankAccountNo = n.BankAccountNo,
            ItemStatus = ItemStatus.GetValueOrDefault(batchId) ?? "PENDING",
        };

        public Task<(IReadOnlyList<PayoutItemView> Items, int Total)> GetItemsAsync(int batchId, PayeeType? payeeType, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            LastItems = (pageSize, payeeType);
            var items = Stored.GetValueOrDefault(batchId) ?? [];
            var matching = items.Where(i => payeeType is null || i.PayeeType == payeeType).ToList();
            var views = matching.Skip((page - 1) * pageSize).Take(pageSize).Select((n, index) => new PayoutItemView(
                ToEntity(batchId, index, n),
                n.PayeeType == PayeeType.Freelancer ? $"Worker {n.WorkerId}" : $"Agency {n.AgencyId}", "ACB")).ToList();
            return Task.FromResult<(IReadOnlyList<PayoutItemView>, int)>((views, matching.Count));
        }

        public Task<IReadOnlyList<PayoutItemView>> GetAllItemsAsync(int batchId, PayeeType payeeType, CancellationToken cancellationToken = default)
        {
            var items = (Stored.GetValueOrDefault(batchId) ?? []).Where(i => i.PayeeType == payeeType).Select((n, index) =>
            {
                var key = new PayeeKey(n.PayeeType, n.PayeeType == PayeeType.Freelancer ? n.WorkerId!.Value : n.AgencyId!.Value);
                var name = Names.GetValueOrDefault(key, ($"Payee {key.Id}", "ACB"));
                var entity = ToEntity(batchId, index, n);
                entity.ItemStatus = "PENDING";
                return new PayoutItemView(entity, name.Item1, name.Item2);
            }).ToList();
            return Task.FromResult<IReadOnlyList<PayoutItemView>>(items);
        }

        public Task<IReadOnlyList<PayoutDetailRow>> GetAgencyDetailAsync(int batchId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PayoutDetailRow>>(Details.ToList());

        public Task SetExportUrlAsync(int batchId, string? url, CancellationToken cancellationToken = default)
        {
            Batches.First(b => b.BatchId == batchId).ExportFileUrl = url;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GetPayeesWithoutBankAsync(int batchId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>((Stored.GetValueOrDefault(batchId) ?? [])
                .Where(i => i.BankAccountNo.Length == 0)
                .Select(n => n.PayeeType == PayeeType.Freelancer ? $"Worker {n.WorkerId}" : $"Agency {n.AgencyId}").ToList());

        public Task<bool> TryCloseAsync(int batchId, decimal totalAmount, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            var batch = Batches.First(b => b.BatchId == batchId);
            if (batch.BatchStatus != "DRAFT") return Task.FromResult(false);
            batch.BatchStatus = "CLOSED";
            batch.ConfirmedBy = null;
            batch.ConfirmedAt = nowUtc;
            batch.TotalAmount = totalAmount;
            ItemStatus[batchId] = "TRANSFERRED";
            return Task.FromResult(true);
        }
    }

    private class Harness
    {
        public MemoryPayouts Repo { get; } = new();
        public MemoryLedger Ledger { get; } = new();
        public LedgerStoreQuery Query { get; }
        public FakeAuditLog Audit { get; } = new();
        public RollbackTrackingUnitOfWork Uow { get; } = new();
        public RecordingPublisher Publisher { get; } = new();
        private readonly DateTime _now;

        public Harness(DateTime? now = null)
        {
            _now = now ?? Now;
            Query = new LedgerStoreQuery(Ledger);
        }

        public PayoutBatchService Service() =>
            new(Repo, Query, new LedgerWriter(Ledger, new TestClock(_now)), Audit, Uow, new TestClock(_now), Publisher, NullLogger<PayoutBatchService>.Instance);

        /// <summary>Writes a row as an earlier event would have, created at the given time (the hash is not part of these tests).</summary>
        public LedgerEntry Add(
            LedgerParty from, LedgerParty to, decimal amount, LedgerEntryType type, LedgerRefType refType, long refId, DateTime? at = null, long? orderId = null)
        {
            var row = new LedgerEntry
            {
                EntryId = Ledger.Rows.Count + 1,
                FromPartyType = from.Type,
                FromPartyId = from.Id,
                ToPartyType = to.Type,
                ToPartyId = to.Id,
                Amount = amount,
                EntryType = type,
                RefType = refType,
                RefId = refId,
                OrderId = orderId,
                IdempotencyKey = $"seed:{Ledger.Rows.Count + 1}",
                CreatedAt = at ?? InOctober,
                EntryHash = new string('0', 64),
                PrevHash = new string('0', 64),
            };
            Ledger.Rows.Add(row);
            return row;
        }

        public static LedgerParty Worker(int id) => new(LedgerPartyType.Worker, id);
        public static LedgerParty Agency(int id) => new(LedgerPartyType.Agency, id);

        /// <summary>A job or an extension: the earning to the payee and the platform's commission, as the event handlers write them.</summary>
        public void Earn(LedgerParty payee, decimal gross, LedgerRefType refType, long refId, decimal rate = 0.20m, DateTime? at = null)
        {
            var commission = Math.Round(gross * rate, 0, MidpointRounding.AwayFromZero);
            Add(LedgerRules.Clearing, payee, gross - commission, refType == LedgerRefType.Assignment ? LedgerEntryType.JobEarning : LedgerEntryType.ExtensionEarning, refType, refId, at);
            Add(LedgerRules.Clearing, LedgerRules.Revenue, commission, LedgerEntryType.Commission, refType, refId, at);
        }

        public void Debit(LedgerParty payee, decimal amount, LedgerEntryType type, long refId, DateTime? at = null) =>
            Add(payee, type == LedgerEntryType.Penalty ? LedgerRules.Revenue : new LedgerParty(LedgerPartyType.External, null), amount, type, LedgerRefType.Entry, refId, at);

        public decimal Balance(LedgerParty party) =>
            Ledger.Rows.Where(e => e.ToPartyType == party.Type && e.ToPartyId == party.Id).Sum(e => e.Amount)
            - Ledger.Rows.Where(e => e.FromPartyType == party.Type && e.FromPartyId == party.Id).Sum(e => e.Amount);

        public IReadOnlyList<LedgerEntry> Settlements => Ledger.Rows.Where(e => e.EntryType == LedgerEntryType.Settlement).ToArray();

        /// <summary>The worked example of the appendix for worker 7 in October.</summary>
        public Harness WithWorkedExample()
        {
            Earn(Worker(7), 260_000m, LedgerRefType.Assignment, 10);
            Earn(Worker(7), 100_000m, LedgerRefType.Extension, 5);
            Debit(Worker(7), 100_000m, LedgerEntryType.Withdrawal, 1);
            Debit(Worker(7), 50_000m, LedgerEntryType.Penalty, 4);
            return this;
        }
    }

    // ---- settle: validation ---------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-13")]
    [InlineData("2026-1")]
    [InlineData("26-10")]
    [InlineData("1999-12")]
    public async Task A_period_that_is_not_YYYY_MM_is_a_400(string? period)
    {
        var h = new Harness();

        var result = await h.Service().SettleAsync(period);

        Assert.Equal(400, result.StatusCode);
        Assert.Contains("periodMonth", result.ValidationErrors!.Keys);
        Assert.Empty(h.Repo.Batches);
    }

    [Theory]
    [InlineData("2026-11")]
    [InlineData("2027-01")]
    public async Task A_month_that_is_not_over_cannot_be_settled(string period)
    {
        var h = new Harness();

        var result = await h.Service().SettleAsync(period);

        Assert.Equal(400, result.StatusCode);
        Assert.Contains("periodMonth", result.ValidationErrors!.Keys);
        Assert.Empty(h.Repo.Batches);
    }

    [Fact]
    public async Task The_demo_switch_settles_a_month_that_is_not_over_as_if_it_ended_now()
    {
        var h = new Harness(); // 2026-11-10: November is running
        h.Earn(Harness.Worker(7), 260_000m, LedgerRefType.Assignment, 1, at: new DateTime(2026, 11, 3, 3, 0, 0, DateTimeKind.Utc));

        Assert.Equal(400, (await h.Service().SettleAsync("2026-11")).StatusCode);
        Assert.Empty(h.Repo.Batches);

        var demo = await h.Service().SettleAsync("2026-11", allowUnfinishedMonth: true);

        Assert.Equal(201, demo.StatusCode);
        Assert.Equal(("CLOSED", 208_000m), (demo.Data!.BatchStatus, demo.Data.TotalAmount));
        Assert.Equal(0m, h.Balance(Harness.Worker(7)));
        Assert.Equal(200, (await h.Service().SettleAsync("2026-11", allowUnfinishedMonth: true)).StatusCode); // and a re-run does nothing
    }

    [Fact]
    public async Task The_month_is_decided_by_the_Asia_Ho_Chi_Minh_calendar_not_UTC()
    {
        // 2026-10-31 18:00 UTC is already 1 November 01:00 in Ho Chi Minh: October is over there, though not yet in UTC.
        var h = new Harness(new DateTime(2026, 10, 31, 18, 0, 0, DateTimeKind.Utc));

        Assert.Equal(201, (await h.Service().SettleAsync("2026-10")).StatusCode);
        Assert.Equal(400, (await h.Service().SettleAsync("2026-11")).StatusCode);
    }

    [Fact]
    public async Task The_entries_of_the_month_are_those_created_between_the_local_first_days_converted_to_UTC()
    {
        var h = new Harness();
        // October in Asia/Ho_Chi_Minh is 2026-09-30T17:00Z .. 2026-10-31T17:00Z (exclusive).
        h.Earn(Harness.Worker(7), 100_000m, LedgerRefType.Assignment, 1, at: new DateTime(2026, 9, 30, 16, 59, 59, DateTimeKind.Utc)); // September
        h.Earn(Harness.Worker(7), 100_000m, LedgerRefType.Assignment, 2, at: new DateTime(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc)); // 00:00 on 1 October
        h.Earn(Harness.Worker(7), 100_000m, LedgerRefType.Assignment, 3, at: new DateTime(2026, 10, 31, 16, 59, 59, DateTimeKind.Utc)); // 23:59:59 on 31 October
        h.Earn(Harness.Worker(7), 100_000m, LedgerRefType.Assignment, 4, at: new DateTime(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc)); // November

        await h.Service().SettleAsync("2026-10");

        var item = Assert.Single(h.Repo.Stored.Values.Single());
        Assert.Equal((2, 200_000m, 40_000m, 80_000m), (item.JobCount, item.GrossAmount, item.CommissionAmount, item.CarryInAmount)); // two jobs of 100,000 gross
        Assert.Equal([2L, 3L], item.AssignmentIds.ToArray());
        Assert.Equal(240_000m, item.NetAmount); // September's 80,000 is the carry-in; the net settles the whole balance at the cut-off
    }

    // ---- settle: the figures --------------------------------------------------------------------

    [Fact]
    public async Task The_worked_example_settles_138000_after_the_advance_and_the_deduction_and_the_wallet_ends_at_zero()
    {
        var h = new Harness().WithWorkedExample();

        var result = await h.Service().SettleAsync("2026-10");

        Assert.Equal(201, result.StatusCode);
        Assert.Equal(("2026-10", "CLOSED", null, 1, 138_000m), (result.Data!.PeriodMonth, result.Data.BatchStatus, result.Data.ConfirmedBy, result.Data.ItemCount, result.Data.TotalAmount));
        Assert.Equal(Now, result.Data.ConfirmedAt);

        var item = Assert.Single(h.Repo.Stored[result.Data.BatchId]);
        Assert.Equal((PayeeType.Freelancer, 7, 1), (item.PayeeType, item.WorkerId, item.JobCount));
        Assert.Equal((360_000m, 72_000m, 50_000m, 100_000m, 0m, 138_000m),
            (item.GrossAmount, item.CommissionAmount, item.PenaltyAmount, item.AdvanceAmount, item.CarryInAmount, item.NetAmount));
        Assert.Equal([10L], item.AssignmentIds.ToArray());

        var settlement = Assert.Single(h.Settlements);
        Assert.Equal((LedgerPartyType.Worker, 7, LedgerPartyType.External, 138_000m, "SETTLEMENT:2026-10:WORKER:7", LedgerRefType.PayoutItem, 1L),
            (settlement.FromPartyType, settlement.FromPartyId!.Value, settlement.ToPartyType, settlement.Amount, settlement.IdempotencyKey, settlement.RefType, settlement.RefId));
        Assert.Equal(0m, h.Balance(Harness.Worker(7)));
        Assert.Equal("TRANSFERRED", h.Repo.ItemStatus[result.Data.BatchId]);
        Assert.Equal(1, h.Repo.Attached.Single().ItemId);
    }

    [Fact]
    public async Task The_system_closes_the_month_audits_it_as_the_system_and_publishes_PayoutBatchClosed_once()
    {
        var h = new Harness().WithWorkedExample();

        var result = await h.Service().SettleAsync("2026-10");

        var audit = Assert.Single(h.Audit.Entries);
        Assert.Equal((AuditActorType.System, (int?)null, "PAYOUT_BATCH", result.Data!.BatchId.ToString(), "batch_status", "DRAFT", "CLOSED"),
            (audit.ActorType, audit.AdminId, audit.EntityType, audit.EntityId, audit.FieldName, audit.OldValue, audit.NewValue));
        var closed = Assert.Single(h.Publisher.Events);
        Assert.Equal((result.Data.BatchId, "2026-10", 138_000m, 1, Now), (closed.BatchId, closed.PeriodMonth, closed.TotalAmount, closed.ItemCount, closed.ClosedAtUtc));
    }

    [Fact]
    public async Task The_balance_identity_holds_for_the_month_carry_in_plus_earnings_less_commission_penalty_and_advance()
    {
        var h = new Harness().WithWorkedExample();
        h.Add(LedgerRules.Clearing, Harness.Worker(7), 30_000m, LedgerEntryType.AbsenceFee, LedgerRefType.Assignment, 11); // no commission on an absence fee
        var entries = await h.Query.GetPartyEntriesAsync(LedgerPartyType.Worker, 7, new DateTime(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc));
        var commissions = await h.Query.GetCommissionsAsync(LedgerRefType.Assignment, [10, 11]);
        var extensionCommissions = await h.Query.GetCommissionsAsync(LedgerRefType.Extension, [5]);

        var figures = PayoutSettlementCalculator.Compute(
            LedgerPartyType.Worker, 7, entries, commissions.Concat(extensionCommissions).ToList(),
            new DateTime(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc), 0m);

        Assert.Equal(0m, figures.Adjustments);
        Assert.Equal(figures.CarryIn + figures.Gross - figures.Commission - figures.Penalty - figures.Advance, figures.BalanceAtCutoff);
        Assert.Equal((390_000m, 72_000m, 168_000m), (figures.Gross, figures.Commission, figures.Net)); // 288,000 + 30,000 - 100,000 - 50,000
    }

    [Fact]
    public async Task A_deduction_larger_than_the_month_makes_no_payment_and_is_carried_into_the_next_month()
    {
        var h = new Harness();
        h.Earn(Harness.Worker(7), 100_000m, LedgerRefType.Assignment, 1); // 80,000 earned
        h.Debit(Harness.Worker(7), 150_000m, LedgerEntryType.Penalty, 4); // balance -70,000

        var october = await h.Service().SettleAsync("2026-10");

        Assert.Equal(0m, october.Data!.TotalAmount);
        var item = Assert.Single(h.Repo.Stored[october.Data.BatchId]);
        Assert.Equal((150_000m, 0m, 0m, -70_000m), (item.PenaltyAmount, item.CarryInAmount, item.NetAmount, h.Balance(Harness.Worker(7))));
        Assert.Empty(h.Settlements); // nothing is paid, nothing is written, and the debt stays in the wallet

        // December: the 240,000 earned in November pays off the 70,000 debt.
        var december = new Harness(new DateTime(2026, 12, 5, 4, 0, 0, DateTimeKind.Utc));
        foreach (var row in h.Ledger.Rows) december.Ledger.Rows.Add(row);
        december.Earn(Harness.Worker(7), 300_000m, LedgerRefType.Assignment, 2, at: new DateTime(2026, 11, 20, 3, 0, 0, DateTimeKind.Utc));
        var next = await december.Service().SettleAsync("2026-11");

        var nextItem = Assert.Single(december.Repo.Stored[next.Data!.BatchId]);
        Assert.Equal((-70_000m, 240_000m, 170_000m), (nextItem.CarryInAmount, nextItem.GrossAmount - nextItem.CommissionAmount, nextItem.NetAmount));
        Assert.Equal(0m, december.Balance(Harness.Worker(7)));
    }

    [Fact]
    public async Task A_payee_with_only_a_debt_and_no_entry_in_the_month_still_gets_an_item_with_no_payment()
    {
        var h = new Harness();
        h.Debit(Harness.Worker(7), 40_000m, LedgerEntryType.Penalty, 4, at: new DateTime(2026, 8, 10, 3, 0, 0, DateTimeKind.Utc)); // August
        h.Earn(Harness.Worker(8), 100_000m, LedgerRefType.Assignment, 1);

        var result = await h.Service().SettleAsync("2026-10");

        Assert.Equal(2, result.Data!.ItemCount);
        var debt = h.Repo.Stored[result.Data.BatchId].Single(i => i.WorkerId == 7);
        Assert.Equal((-40_000m, 0m, 0), (debt.CarryInAmount, debt.NetAmount, debt.JobCount));
        Assert.Equal([8], h.Settlements.Select(s => s.FromPartyId!.Value).ToArray());
    }

    [Fact]
    public async Task A_pending_withdrawal_at_the_cut_off_stays_in_the_wallet_and_is_not_paid()
    {
        var h = new Harness();
        h.Earn(Harness.Worker(7), 360_000m, LedgerRefType.Assignment, 1); // 288,000 earned
        h.Query.Held[7] = 100_000m; // a request waiting for the Admin

        var result = await h.Service().SettleAsync("2026-10");

        Assert.Equal(188_000m, Assert.Single(h.Settlements).Amount); // 288,000 - 100,000 held
        Assert.Equal(100_000m, h.Balance(Harness.Worker(7))); // exactly the held amount: approving it later is next month's advance
        Assert.Equal(0m, h.Repo.Stored[result.Data!.BatchId].Single().AdvanceAmount);
    }

    [Fact]
    public async Task An_agency_is_settled_as_one_item_with_its_own_rate_and_no_withdrawal_held()
    {
        var h = new Harness();
        h.Earn(Harness.Agency(3), 300_000m, LedgerRefType.Assignment, 20, rate: 0.15m);
        h.Earn(Harness.Agency(3), 300_000m, LedgerRefType.Assignment, 21, rate: 0.15m);
        h.Add(LedgerRules.Clearing, Harness.Agency(3), 50_000m, LedgerEntryType.AbsenceFee, LedgerRefType.Assignment, 22);
        h.Debit(Harness.Agency(3), 20_000m, LedgerEntryType.Penalty, 9);
        h.Repo.Banks[new PayeeKey(PayeeType.Agency, 3)] = "333";

        var result = await h.Service().SettleAsync("2026-10");

        var item = Assert.Single(h.Repo.Stored[result.Data!.BatchId]);
        Assert.Equal((PayeeType.Agency, 3, 3), (item.PayeeType, item.AgencyId, item.JobCount));
        Assert.Equal((650_000m, 90_000m, 20_000m, 0m, 540_000m, "333"),
            (item.GrossAmount, item.CommissionAmount, item.PenaltyAmount, item.AdvanceAmount, item.NetAmount, item.BankAccountNo));
        Assert.Equal([20L, 21L, 22L], item.AssignmentIds.ToArray());
        var settlement = Assert.Single(h.Settlements);
        Assert.Equal((LedgerPartyType.Agency, 3, "SETTLEMENT:2026-10:AGENCY:3", 540_000m), (settlement.FromPartyType, settlement.FromPartyId!.Value, settlement.IdempotencyKey, settlement.Amount));
        Assert.Equal(0m, h.Balance(Harness.Agency(3)));
    }

    [Fact]
    public async Task Each_payee_gets_one_item_freelancers_before_agencies_and_the_total_is_the_sum_of_the_nets()
    {
        var h = new Harness();
        h.Earn(Harness.Agency(3), 200_000m, LedgerRefType.Assignment, 1, rate: 0.15m);
        h.Earn(Harness.Worker(9), 100_001m, LedgerRefType.Assignment, 2);
        h.Earn(Harness.Worker(5), 260_000m, LedgerRefType.Assignment, 3);

        var result = await h.Service().SettleAsync("2026-10");

        Assert.Equal(3, result.Data!.ItemCount);
        Assert.Equal([5, 9], h.Repo.Stored[result.Data.BatchId].Where(i => i.PayeeType == PayeeType.Freelancer).Select(i => i.WorkerId!.Value).ToArray());
        Assert.Equal(208_000m + 80_001m + 170_000m, result.Data.TotalAmount);
        Assert.Equal(h.Settlements.Sum(s => s.Amount), result.Data.TotalAmount);
        Assert.Equal(3, h.Repo.Locked.Count); // every payee under its wallet lock
    }

    [Fact]
    public async Task A_month_without_any_entry_closes_an_empty_batch_and_still_publishes_the_event()
    {
        var h = new Harness();

        var result = await h.Service().SettleAsync("2026-10");

        Assert.Equal(201, result.StatusCode);
        Assert.Equal(("CLOSED", 0, 0m), (result.Data!.BatchStatus, result.Data.ItemCount, result.Data.TotalAmount));
        Assert.Empty(h.Settlements);
        Assert.Equal(0, Assert.Single(h.Publisher.Events).ItemCount);
    }

    // ---- settle: re-runs and failures -----------------------------------------------------------

    [Fact]
    public async Task Running_a_closed_month_again_does_nothing_answers_200_and_publishes_nothing_more()
    {
        var h = new Harness().WithWorkedExample();
        var first = await h.Service().SettleAsync("2026-10");
        var rows = h.Ledger.Rows.Count;

        var again = await h.Service().SettleAsync("2026-10");

        Assert.Equal(200, again.StatusCode);
        Assert.Equal((first.Data!.BatchId, "CLOSED", 138_000m), (again.Data!.BatchId, again.Data.BatchStatus, again.Data.TotalAmount));
        Assert.Equal(rows, h.Ledger.Rows.Count);
        Assert.Single(h.Settlements);
        Assert.Single(h.Publisher.Events);
        Assert.Single(h.Audit.Entries);
        Assert.Single(h.Repo.Batches);
    }

    [Fact]
    public async Task A_payee_that_fails_leaves_the_month_open_and_the_next_run_settles_only_the_rest()
    {
        var h = new Harness();
        h.Earn(Harness.Worker(7), 260_000m, LedgerRefType.Assignment, 1);
        h.Earn(Harness.Worker(8), 100_000m, LedgerRefType.Assignment, 2);
        h.Repo.FailLockOnce = new PayeeKey(PayeeType.Freelancer, 8); // the wallet lock of worker 8 times out

        var first = await h.Service().SettleAsync("2026-10");

        Assert.Equal(409, first.StatusCode);
        Assert.Equal("DRAFT", h.Repo.Batches.Single().BatchStatus);
        Assert.Equal([7], h.Settlements.Select(s => s.FromPartyId!.Value).ToArray());
        Assert.Empty(h.Publisher.Events);

        var second = await h.Service().SettleAsync("2026-10");

        Assert.Equal(201, second.StatusCode);
        Assert.Equal("CLOSED", second.Data!.BatchStatus);
        Assert.Equal([7, 8], h.Settlements.Select(s => s.FromPartyId!.Value).Order().ToArray()); // worker 7 was not settled twice
        Assert.Equal(208_000m + 80_000m, second.Data.TotalAmount);
        Assert.Single(h.Publisher.Events);
    }

    [Fact]
    public async Task When_the_month_lock_cannot_be_taken_the_answer_is_a_409_and_nothing_is_stored()
    {
        var h = new Harness().WithWorkedExample();
        h.Repo.LockWorks = false;

        var result = await h.Service().SettleAsync("2026-10");

        Assert.Equal(409, result.StatusCode);
        Assert.Empty(h.Repo.Batches);
        Assert.Empty(h.Settlements);
        Assert.Equal(1, h.Uow.RolledBack);
    }

    [Fact]
    public async Task Nothing_in_the_ledger_changes_when_a_month_has_no_net_to_pay_and_other_entries_are_untouched()
    {
        var h = new Harness().WithWorkedExample();
        var before = h.Ledger.Rows.Select(r => (r.IdempotencyKey, r.Amount)).ToList();

        await h.Service().SettleAsync("2026-10");

        Assert.Equal(before, h.Ledger.Rows.Take(before.Count).Select(r => (r.IdempotencyKey, r.Amount)).ToList()); // append only
        Assert.Equal(before.Count + 1, h.Ledger.Rows.Count);
    }

    // ---- read -----------------------------------------------------------------------------------

    [Fact]
    public async Task The_list_is_newest_month_first_with_the_item_counts_and_validates_paging()
    {
        var h = new Harness();
        h.Earn(Harness.Worker(5), 260_000m, LedgerRefType.Assignment, 1, at: new DateTime(2026, 9, 10, 3, 0, 0, DateTimeKind.Utc));
        await h.Service().SettleAsync("2026-09");
        h.Earn(Harness.Worker(5), 260_000m, LedgerRefType.Assignment, 2);
        await h.Service().SettleAsync("2026-10");

        var result = await h.Service().ListAsync(null, null);

        Assert.Equal(["2026-10", "2026-09"], result.Data!.Items.Select(b => b.PeriodMonth).ToArray());
        Assert.Equal(1, result.Data.Items[0].ItemCount);
        Assert.Equal((1, 20, 2), (result.Data.Page, result.Data.PageSize, result.Data.Total));

        Assert.Equal(400, (await h.Service().ListAsync("0", null)).StatusCode);
        Assert.Equal(400, (await h.Service().ListAsync(null, "101")).StatusCode);
        Assert.Equal(400, (await h.Service().ListAsync("x", null)).StatusCode);
    }

    [Fact]
    public async Task The_detail_has_the_items_with_advance_and_carry_in_a_payee_type_filter_and_a_warning_for_each_payee_without_a_bank_account()
    {
        var h = new Harness();
        h.Earn(Harness.Worker(5), 260_000m, LedgerRefType.Assignment, 1);
        h.Earn(Harness.Worker(6), 260_000m, LedgerRefType.Assignment, 2);
        h.Earn(Harness.Agency(3), 100_000m, LedgerRefType.Assignment, 3, rate: 0.15m);
        h.Debit(Harness.Worker(5), 100_000m, LedgerEntryType.Withdrawal, 1);
        h.Repo.Banks[new PayeeKey(PayeeType.Freelancer, 5)] = "111";
        h.Repo.Banks[new PayeeKey(PayeeType.Agency, 3)] = "333";
        var settled = await h.Service().SettleAsync("2026-10");

        var all = await h.Service().GetAsync(settled.Data!.BatchId, null, null, null);
        var agencyOnly = await h.Service().GetAsync(settled.Data.BatchId, "agency", null, null);

        Assert.Equal(3, all.Data!.Total);
        Assert.Equal(3, all.Data.Batch.ItemCount);
        Assert.Equal(["Worker 6 has no bank account number."], all.Data.Warnings.ToArray());
        Assert.Equal("Worker 5", all.Data.Items[0].PayeeName);
        Assert.Equal("FREELANCER", all.Data.Items[0].PayeeType);
        Assert.Equal((100_000m, 0m, 108_000m), (all.Data.Items[0].AdvanceAmount, all.Data.Items[0].CarryInAmount, all.Data.Items[0].NetAmount));
        Assert.Equal("TRANSFERRED", all.Data.Items[0].ItemStatus);
        Assert.Null(all.Data.Batch.ConfirmedBy);
        var agency = Assert.Single(agencyOnly.Data!.Items);
        Assert.Equal("AGENCY", agency.PayeeType);
        Assert.Equal(PayeeType.Agency, h.Repo.LastItems!.Value.Type);
    }

    [Theory]
    [InlineData("WORKER", null, null, "payeeType")]
    [InlineData(null, "0", null, "page")]
    [InlineData(null, null, "101", "pageSize")]
    public async Task Bad_detail_query_values_are_a_400(string? type, string? page, string? pageSize, string field)
    {
        var h = new Harness();
        var settled = await h.Service().SettleAsync("2026-10");

        var result = await h.Service().GetAsync(settled.Data!.BatchId, type, page, pageSize);

        Assert.Equal(400, result.StatusCode);
        Assert.Contains(field, result.ValidationErrors!.Keys);
    }

    [Fact]
    public async Task A_missing_batch_is_a_404()
    {
        Assert.Equal(404, (await new Harness().Service().GetAsync(99, null, null, null)).StatusCode);
    }

    // ---- controller -----------------------------------------------------------------------------

    private class StubBatches(int status) : IPayoutBatchService
    {
        private PayoutResult<T> Answer<T>(T data) => status switch
        {
            200 => PayoutResult<T>.Ok(data),
            400 => PayoutResult<T>.ValidationError(new Dictionary<string, string[]> { ["page"] = ["bad"] }),
            404 => PayoutResult<T>.NotFound(),
            _ => PayoutResult<T>.Conflict("closed"),
        };

        public Task<PayoutResult<PayoutBatchDto>> SettleAsync(string? periodMonth, bool allowUnfinishedMonth = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(Answer(new PayoutBatchDto()));

        public Task<PayoutResult<PayoutBatchPageDto>> ListAsync(string? page, string? pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(Answer(new PayoutBatchPageDto()));

        public Task<PayoutResult<PayoutBatchDetailDto>> GetAsync(int batchId, string? payeeType, string? page, string? pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(Answer(new PayoutBatchDetailDto()));
    }

    [Fact]
    public void The_controller_is_admin_only_and_only_reads_the_build_and_confirm_routes_are_gone()
    {
        var type = typeof(AdminPayoutBatchesController);
        Assert.Equal("AdminOnly", type.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal("api/v1/admin/payout-batches", type.GetCustomAttribute<RouteAttribute>()!.Template);

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.All(methods, m => Assert.Null(m.GetCustomAttribute<AllowAnonymousAttribute>()));
        var routes = methods
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>().Select(a => $"{a.HttpMethods.Single()} {a.Template}".Trim()))
            .Order().ToArray();
        Assert.Equal(["GET", "GET {batchId:int}"], routes);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(400)]
    public async Task List_and_detail_pass_the_service_status_through(int status)
    {
        var controller = new AdminPayoutBatchesController(new StubBatches(status));

        Assert.Equal(status, Assert.IsType<ObjectResult>(await controller.List(null, null, default)).StatusCode);
        Assert.Equal(status, Assert.IsType<ObjectResult>(await controller.Get(5, null, null, null, default)).StatusCode);
    }

    [Fact]
    public async Task A_missing_batch_is_a_404_through_the_controller_and_keeps_the_envelope()
    {
        var result = Assert.IsType<ObjectResult>(await new AdminPayoutBatchesController(new StubBatches(404)).Get(5, null, null, null, default));

        Assert.Equal(404, result.StatusCode);
        Assert.False(Assert.IsType<ApiResponse<object>>(result.Value).Success);
    }
}
