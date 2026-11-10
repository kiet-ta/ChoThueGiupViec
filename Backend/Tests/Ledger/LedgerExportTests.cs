using CommonService.Application.Exceptions;
using CommonService.Application.Features.Ledger;
using CommonService.Application.Features.Payouts;
using CommonService.Application.Features.Payouts.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.Tests.Payouts;
using CommonService.WebAPI.Controllers.Ledger;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Tests.Ledger;

/// <summary>The xlsx statements of the ledger and the worker's monthly statement (ticket LEDGER-12).</summary>
public class LedgerExportTests
{
    private const int Worker = 7;
    private const int Agency = 3;
    private static readonly DateTime Now = new(2026, 11, 10, 4, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OctoberLast = new(2026, 10, 31, 16, 59, 59, DateTimeKind.Utc); // 31 October 23:59:59 in Ho Chi Minh
    private static readonly DateTime NovemberFirst = new(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc); // 1 November 00:00:00
    private static readonly DateTime NovemberMiddle = new(2026, 11, 5, 3, 30, 0, DateTimeKind.Utc); // 5 November 10:30
    private static readonly LedgerParty External = new(LedgerPartyType.External, null);

    private class Rig
    {
        public MemoryLedger Ledger { get; } = new();
        public FakeClock Clock { get; } = new();
        public MemoryHistoryQuery Query { get; }

        public Rig()
        {
            Clock.Set(Now);
            Query = new MemoryHistoryQuery(Ledger);
        }

        public LedgerExportService Export() => new(Query, Clock);

        public LedgerEntry Add(LedgerParty from, LedgerParty to, decimal amount, LedgerEntryType type, DateTime at, LedgerRefType refType = LedgerRefType.Assignment, long refId = 1)
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
                IdempotencyKey = $"seed:{Ledger.Rows.Count + 1}",
                CreatedAt = at,
                EntryHash = new string('a', 63) + (Ledger.Rows.Count + 1),
                PrevHash = new string('0', 64),
            };
            typeof(LedgerEntry).GetProperty(nameof(LedgerEntry.EntryCode))!.SetValue(row, $"LG{row.EntryId:00000000}");
            Ledger.Rows.Add(row);
            return row;
        }

        public static LedgerParty W(int id) => new(LedgerPartyType.Worker, id);
        public static LedgerParty A(int id) => new(LedgerPartyType.Agency, id);
        public static LedgerParty E(int id) => new(LedgerPartyType.AgencyEscrow, id);
    }

    private static LedgerHistoryRequest Req(string? from = null, string? to = null, string? type = null, string? partyType = null, int? partyId = null, int? page = null, int? pageSize = null) =>
        new(from, to, type, page, pageSize, partyType, partyId);

    private static List<string?[]> Table(LedgerExportFile file) => XlsxTestReader.Table(file.Content, 8);

    // ---- Admin ----------------------------------------------------------------------------------

    [Fact]
    public async Task The_admin_file_has_the_columns_one_row_per_entry_oldest_first_in_Vietnam_time_with_the_hash()
    {
        var rig = new Rig();
        rig.Add(LedgerRules.Customer(12), LedgerRules.Clearing, 260_000m, LedgerEntryType.OrderPaid, NovemberMiddle, LedgerRefType.Order, 41);
        rig.Add(LedgerRules.Clearing, Rig.W(Worker), 208_000m, LedgerEntryType.JobEarning, NovemberMiddle.AddHours(1), LedgerRefType.Assignment, 10);

        var file = await rig.Export().ExportAdminAsync(Req());

        Assert.Equal("ledger-start_now.xlsx", file.FileName);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);
        var table = Table(file);
        Assert.Equal(["entry_code", "created_at", "entry_type", "from", "to", "amount", "reference", "entry_hash"], table[0]);
        Assert.Equal(3, table.Count); // header + 2 entries; no account selected, so no balance rows
        Assert.Equal(["LG00000001", "2026-11-05T10:30:00+07:00", "ORDER_PAID", "EXTERNAL:12", "PLATFORM_CLEARING", "260000", "ORDER:41", new string('a', 63) + "1"], table[1]);
        Assert.Equal(["LG00000002", "2026-11-05T11:30:00+07:00", "JOB_EARNING", "PLATFORM_CLEARING", "WORKER:7", "208000", "ASSIGNMENT:10", new string('a', 63) + "2"], table[2]);
        Assert.Equal("Ledger", XlsxTestReader.SheetName(file.Content));
        Assert.Equal("inlineStr", XlsxTestReader.Rows(file.Content)[1][0].Type); // the code is text
        Assert.Equal("2", XlsxTestReader.Rows(file.Content)[1][5].Style); // the amount is a number
    }

    [Fact]
    public async Task With_one_account_selected_the_file_opens_and_closes_with_its_balance_over_the_vietnam_days()
    {
        var rig = new Rig();
        rig.Add(LedgerRules.Clearing, Rig.W(Worker), 1_000m, LedgerEntryType.JobEarning, OctoberLast);
        rig.Add(LedgerRules.Clearing, Rig.W(Worker), 2_000m, LedgerEntryType.JobEarning, NovemberFirst);
        rig.Add(Rig.W(Worker), External, 500m, LedgerEntryType.Withdrawal, NovemberMiddle, LedgerRefType.Withdrawal, 1);
        rig.Add(LedgerRules.Clearing, Rig.W(Worker), 9_000m, LedgerEntryType.JobEarning, Now.AddDays(30));
        rig.Add(LedgerRules.Clearing, Rig.W(8), 777m, LedgerEntryType.JobEarning, NovemberMiddle); // another worker

        var file = await rig.Export().ExportAdminAsync(Req(from: "2026-11-01", to: "2026-11-30", partyType: "WORKER", partyId: Worker));

        Assert.Equal("ledger-2026-11-01_2026-11-30.xlsx", file.FileName);
        var table = Table(file);
        Assert.Equal(5, table.Count);
        Assert.Equal(["OPENING_BALANCE", "2026-11-01T00:00:00+07:00", "BALANCE", "WORKER:7", null, "1000", null, null], table[1]);
        Assert.Equal(["LG00000002", "LG00000003"], new[] { table[2][0]!, table[3][0]! }); // the November entries of this worker, oldest first
        Assert.Equal(["CLOSING_BALANCE", "2026-12-01T00:00:00+07:00", "BALANCE", "WORKER:7", null, "2500", null, null], table[4]); // 1,000 + 2,000 - 500
    }

    [Fact]
    public async Task A_platform_account_has_balance_rows_too_and_a_type_of_party_without_an_id_has_none()
    {
        var rig = new Rig();
        rig.Add(LedgerRules.Clearing, LedgerRules.Revenue, 52_000m, LedgerEntryType.Commission, NovemberMiddle);
        rig.Add(LedgerRules.Clearing, Rig.W(Worker), 208_000m, LedgerEntryType.JobEarning, NovemberMiddle);

        var revenue = Table(await rig.Export().ExportAdminAsync(Req(partyType: "PLATFORM_REVENUE")));
        Assert.Equal(["OPENING_BALANCE", null, "BALANCE", "PLATFORM_REVENUE", null, "0", null, null], revenue[1]);
        Assert.Equal(["CLOSING_BALANCE", null, "BALANCE", "PLATFORM_REVENUE", null, "52000", null, null], revenue[^1]);
        Assert.Equal(4, revenue.Count);

        var anyWorker = Table(await rig.Export().ExportAdminAsync(Req(partyType: "WORKER")));
        Assert.Equal(2, anyWorker.Count); // header + the one entry of any worker, no balance rows
    }

    [Fact]
    public async Task The_type_filter_limits_the_rows_but_not_the_balances_and_paging_values_are_ignored()
    {
        var rig = new Rig();
        rig.Add(LedgerRules.Clearing, Rig.W(Worker), 2_000m, LedgerEntryType.JobEarning, NovemberFirst);
        rig.Add(Rig.W(Worker), External, 500m, LedgerEntryType.Withdrawal, NovemberMiddle, LedgerRefType.Withdrawal, 1);

        var table = Table(await rig.Export().ExportAdminAsync(Req(type: "withdrawal", partyType: "WORKER", partyId: Worker, page: 0, pageSize: 1000)));

        Assert.Equal(4, table.Count);
        Assert.Equal("WITHDRAWAL", table[2][2]);
        Assert.Equal("1500", table[3][5]); // the closing balance counts every entry
    }

    [Theory]
    [InlineData("2026-13-01", null, null, "from")]
    [InlineData(null, "yesterday", null, "to")]
    [InlineData(null, null, "SALARY", "type")]
    public async Task Bad_filters_are_a_400_with_the_field_named(string? from, string? to, string? type, string field)
    {
        var error = await Assert.ThrowsAsync<ValidationException>(() => new Rig().Export().ExportAdminAsync(Req(from, to, type)));

        Assert.Contains(field, error.Errors.Keys);
    }

    [Fact]
    public async Task A_statement_of_more_than_the_maximum_number_of_entries_must_be_narrowed()
    {
        var rig = new Rig();
        for (var i = 0; i < LedgerExportService.MaxRows + 1; i++) rig.Add(LedgerRules.Clearing, Rig.W(Worker), 1m, LedgerEntryType.JobEarning, NovemberMiddle);

        var error = await Assert.ThrowsAsync<ValidationException>(() => rig.Export().ExportAdminAsync(Req()));

        Assert.Contains("from", error.Errors.Keys);
    }

    // ---- Agency ---------------------------------------------------------------------------------

    [Fact]
    public async Task The_agency_file_has_the_wallet_and_the_escrow_rows_with_their_balances_and_nothing_of_other_parties()
    {
        var rig = new Rig();
        rig.Add(LedgerRules.Clearing, Rig.A(Agency), 221_000m, LedgerEntryType.JobEarning, OctoberLast);
        rig.Add(new LedgerParty(LedgerPartyType.External, Agency), Rig.E(Agency), 2_000_000m, LedgerEntryType.EscrowDeposit, NovemberFirst, LedgerRefType.EscrowTxn, 5);
        rig.Add(Rig.E(Agency), LedgerRules.Revenue, 300_000m, LedgerEntryType.EscrowPenalty, NovemberMiddle, LedgerRefType.EscrowTxn, 6);
        rig.Add(LedgerRules.Clearing, Rig.A(4), 999_000m, LedgerEntryType.JobEarning, NovemberMiddle); // another agency
        rig.Add(LedgerRules.Clearing, Rig.W(Agency), 888_000m, LedgerEntryType.JobEarning, NovemberMiddle); // a worker with the same id

        var file = await rig.Export().ExportAgencyAsync(Agency, Req(from: "2026-11-01", to: "2026-11-30"));

        Assert.Equal($"ledger-agency-{Agency}-2026-11-01_2026-11-30.xlsx", file.FileName);
        var table = Table(file);
        Assert.Equal(["OPENING_BALANCE", "2026-11-01T00:00:00+07:00", "BALANCE", "AGENCY:3", null, "221000", null, null], table[1]);
        Assert.Equal(["OPENING_BALANCE", "2026-11-01T00:00:00+07:00", "BALANCE", "AGENCY_ESCROW:3", null, "0", null, null], table[2]);
        Assert.Equal(["ESCROW_DEPOSIT", "ESCROW_PENALTY"], new[] { table[3][2]!, table[4][2]! });
        Assert.Equal(["CLOSING_BALANCE", "2026-12-01T00:00:00+07:00", "BALANCE", "AGENCY:3", null, "221000", null, null], table[5]);
        Assert.Equal(["CLOSING_BALANCE", "2026-12-01T00:00:00+07:00", "BALANCE", "AGENCY_ESCROW:3", null, "1700000", null, null], table[6]);
        Assert.Equal(7, table.Count);
        Assert.DoesNotContain(table, row => row[5] is "999000" or "888000");
    }

    // ---- controllers ----------------------------------------------------------------------------

    private class StubExport : ILedgerExportService
    {
        public LedgerHistoryRequest? Last { get; private set; }
        public int? Agency { get; private set; }

        public Task<LedgerExportFile> ExportAdminAsync(LedgerHistoryRequest request, CancellationToken cancellationToken = default)
        {
            Last = request;
            return Task.FromResult(new LedgerExportFile([1, 2, 3], "ledger-admin.xlsx", XlsxWriter.ContentType));
        }

        public Task<LedgerExportFile> ExportAgencyAsync(int agencyId, LedgerHistoryRequest request, CancellationToken cancellationToken = default)
        {
            (Agency, Last) = (agencyId, request);
            return Task.FromResult(new LedgerExportFile([4, 5], "ledger-agency.xlsx", XlsxWriter.ContentType));
        }
    }

    private class StubUser(int? id) : ICurrentUser
    {
        public bool IsAuthenticated => id is not null;
        public int? UserId => id;
        public UserRole? Role => null;
    }

    private class NoReconciliation : ILedgerReconciliationService
    {
        public Task<LedgerReconciliationReport> ReportAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LedgerSweepResult> SweepAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task The_endpoints_answer_the_file_itself_with_the_attachment_name_and_the_agency_comes_from_the_token()
    {
        var export = new StubExport();

        var admin = Assert.IsType<FileContentResult>(await new AdminLedgerController(new NoReconciliation(), null!, export).Export("2026-11-01", null, "COMMISSION", "WORKER", 7, default));
        Assert.Equal(("ledger-admin.xlsx", XlsxWriter.ContentType, 3), (admin.FileDownloadName, admin.ContentType, admin.FileContents.Length));
        Assert.Equal(new LedgerHistoryRequest("2026-11-01", null, "COMMISSION", null, null, "WORKER", 7), export.Last);

        var agency = Assert.IsType<FileContentResult>(await new AgencyLedgerController(null!, export, new StubUser(Agency)).Export(null, "2026-11-30", null, default));
        Assert.Equal("ledger-agency.xlsx", agency.FileDownloadName);
        Assert.Equal(Agency, export.Agency);
        Assert.IsType<UnauthorizedObjectResult>(await new AgencyLedgerController(null!, export, new StubUser(null)).Export(null, null, null, default));
    }

    // ---- the worker's monthly statement ---------------------------------------------------------

    /// <summary>The payout tables and the ledger of one settled month, as the settlement leaves them.</summary>
    private class SettledMonth
    {
        public MemoryLedger Ledger { get; } = new();
        public PayoutBatchServiceTests.MemoryPayouts Payouts { get; } = new();
        public MemoryWithdrawals Workers { get; } = new();
        public FakeClock Clock { get; } = new();

        public SettledMonth(DateTime now)
        {
            Clock.Set(now);
        }

        public LedgerEntry Add(LedgerParty from, LedgerParty to, decimal amount, LedgerEntryType type, LedgerRefType refType, long refId, DateTime at)
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
                IdempotencyKey = $"seed:{Ledger.Rows.Count + 1}",
                CreatedAt = at,
                EntryHash = new string('0', 64),
                PrevHash = new string('0', 64),
            };
            Ledger.Rows.Add(row);
            return row;
        }

        public SettledMonth WorkedExample()
        {
            var at = new DateTime(2026, 10, 15, 3, 0, 0, DateTimeKind.Utc);
            Add(LedgerRules.Clearing, Rig.W(Worker), 208_000m, LedgerEntryType.JobEarning, LedgerRefType.Assignment, 10, at);
            Add(LedgerRules.Clearing, LedgerRules.Revenue, 52_000m, LedgerEntryType.Commission, LedgerRefType.Assignment, 10, at);
            Add(LedgerRules.Clearing, Rig.W(Worker), 80_000m, LedgerEntryType.ExtensionEarning, LedgerRefType.Extension, 5, at);
            Add(LedgerRules.Clearing, LedgerRules.Revenue, 20_000m, LedgerEntryType.Commission, LedgerRefType.Extension, 5, at);
            Add(Rig.W(Worker), External, 100_000m, LedgerEntryType.Withdrawal, LedgerRefType.Withdrawal, 1, at.AddDays(2));
            Add(Rig.W(Worker), LedgerRules.Revenue, 50_000m, LedgerEntryType.Penalty, LedgerRefType.Dispute, 4, at.AddDays(3));
            return this;
        }

        public Task<CommonService.Application.Features.Payouts.Dtos.PayoutBatchDto> SettleAsync(string period) => SettleCoreAsync(period);

        private async Task<CommonService.Application.Features.Payouts.Dtos.PayoutBatchDto> SettleCoreAsync(string period)
        {
            var service = new PayoutBatchService(
                Payouts, new LedgerStoreQuery(Ledger), new LedgerWriter(Ledger, Clock), new FakeAuditLog(), new PassThroughUnitOfWork(), Clock,
                new NullPublisher(), NullLogger<PayoutBatchService>.Instance);
            return (await service.SettleAsync(period)).Data!;
        }

        public LedgerHistoryService Statement() =>
            new(new MemoryHistoryQuery(Ledger), Workers, new SettledEarnings(Payouts), new LedgerChainVerifier(Ledger), Clock);
    }

    private class PassThroughUnitOfWork : CommonService.Application.Interfaces.IRepositories.IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default) => action();
    }

    private class NullPublisher : MediatR.IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : MediatR.INotification => Task.CompletedTask;
    }

    /// <summary>The payout item of a worker for a month, read from the in-memory payout tables.</summary>
    private class SettledEarnings(PayoutBatchServiceTests.MemoryPayouts payouts) : IWorkerEarningsRepository
    {
        public Task<WorkerType?> GetWorkerTypeAsync(int workerId, CancellationToken cancellationToken = default) => Task.FromResult<WorkerType?>(WorkerType.Freelancer);

        public Task<WorkerBatchState?> GetBatchStateAsync(int workerId, string periodMonth, CancellationToken cancellationToken = default)
        {
            var batch = payouts.Batches.FirstOrDefault(b => b.PeriodMonth == periodMonth);
            if (batch is null) return Task.FromResult<WorkerBatchState?>(null);
            var item = payouts.Stored.GetValueOrDefault(batch.BatchId)?.FirstOrDefault(i => i.WorkerId == workerId);
            return Task.FromResult<WorkerBatchState?>(new WorkerBatchState(
                batch.BatchId, batch.BatchStatus,
                item is null ? null : new WorkerBatchItem(item.JobCount, item.GrossAmount, item.CommissionAmount, item.PenaltyAmount, item.AdvanceAmount, item.CarryInAmount, item.NetAmount)));
        }

        public Task<(IReadOnlyList<WorkerPayoutHistoryRow> Items, int Total)> GetClosedItemsAsync(int workerId, int page, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task The_statement_of_a_settled_month_matches_its_payout_item_and_ends_at_zero()
    {
        var month = new SettledMonth(Now).WorkedExample();
        var batch = await month.SettleAsync("2026-10");
        var item = Assert.Single(month.Payouts.Stored[batch.BatchId]);

        var statement = await month.Statement().GetWorkerStatementAsync(Worker, "2026-10");

        Assert.Equal("2026-10", statement.PeriodMonth);
        Assert.Equal((0m, item.AdvanceAmount, item.PenaltyAmount, item.NetAmount, 0m), (statement.OpeningBalance, statement.AdvanceAmount, statement.PenaltyAmount, statement.SettlementAmount, statement.ClosingBalance));
        Assert.Equal((100_000m, 50_000m, 138_000m), (statement.AdvanceAmount, statement.PenaltyAmount, statement.SettlementAmount));
        Assert.Equal(["JOB_EARNING", "EXTENSION_EARNING", "WITHDRAWAL", "PENALTY", "SETTLEMENT"], statement.Items.Select(i => i.EntryType).ToArray()); // oldest first, the settlement last
        Assert.Equal([208_000m, 80_000m, -100_000m, -50_000m, -138_000m], statement.Items.Select(i => i.SignedAmount!.Value).ToArray());
        Assert.Equal(statement.Items[^1].EntryHash, statement.LastHash);
        Assert.NotEqual(new string('0', 64), statement.LastHash); // the settlement entry was written by the real writer, so it carries a real hash
        Assert.Equal(0m, statement.Items.Sum(i => i.SignedAmount!.Value) + statement.OpeningBalance - statement.ClosingBalance); // the rows lead from the opening to the closing balance
    }

    [Fact]
    public async Task A_month_not_settled_yet_shows_its_entries_and_no_settlement_and_the_balance_stays()
    {
        var month = new SettledMonth(Now).WorkedExample();

        var statement = await month.Statement().GetWorkerStatementAsync(Worker, "2026-10");

        Assert.Equal((100_000m, 50_000m, 0m, 138_000m), (statement.AdvanceAmount, statement.PenaltyAmount, statement.SettlementAmount, statement.ClosingBalance)); // 288,000 - 100,000 - 50,000
        Assert.DoesNotContain(statement.Items, i => i.EntryType == "SETTLEMENT");
    }

    [Fact]
    public async Task The_opening_balance_is_the_carry_in_and_the_current_month_is_allowed()
    {
        var month = new SettledMonth(Now);
        month.Add(LedgerRules.Clearing, Rig.W(Worker), 70_000m, LedgerEntryType.JobEarning, LedgerRefType.Assignment, 1, new DateTime(2026, 9, 3, 3, 0, 0, DateTimeKind.Utc));
        month.Add(LedgerRules.Clearing, Rig.W(Worker), 30_000m, LedgerEntryType.JobEarning, LedgerRefType.Assignment, 2, NovemberMiddle);

        var statement = await month.Statement().GetWorkerStatementAsync(Worker, "2026-11");

        Assert.Equal((70_000m, 100_000m, 1), (statement.OpeningBalance, statement.ClosingBalance, statement.Items.Count));
        Assert.Equal(0m, statement.SettlementAmount);
    }

    [Theory]
    [InlineData("2026-12")]
    [InlineData("2026-13")]
    [InlineData("November")]
    [InlineData("")]
    public async Task A_future_or_malformed_month_is_a_400(string month)
    {
        var error = await Assert.ThrowsAsync<ValidationException>(() => new SettledMonth(Now).Statement().GetWorkerStatementAsync(Worker, month));

        Assert.Contains("month", error.Errors.Keys);
    }

    [Fact]
    public async Task Agency_staff_get_403_and_an_unknown_worker_404_and_other_workers_money_is_never_in_the_statement()
    {
        var month = new SettledMonth(Now).WorkedExample();
        month.Add(LedgerRules.Clearing, Rig.W(8), 777m, LedgerEntryType.JobEarning, LedgerRefType.Assignment, 99, new DateTime(2026, 10, 15, 3, 0, 0, DateTimeKind.Utc));
        month.Workers.Workers[9] = new WithdrawalWorker(WorkerType.AgencyStaff, null, null);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => month.Statement().GetWorkerStatementAsync(9, "2026-10"));
        await Assert.ThrowsAsync<NotFoundException>(() => month.Statement().GetWorkerStatementAsync(404, "2026-10"));
        var statement = await month.Statement().GetWorkerStatementAsync(Worker, "2026-10");
        Assert.DoesNotContain(statement.Items, i => i.Amount == 777m);
    }
}
