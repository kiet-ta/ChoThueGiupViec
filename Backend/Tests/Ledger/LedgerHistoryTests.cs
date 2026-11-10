using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Ledger;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.WebAPI.Controllers.Ledger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.Tests.Ledger;

/// <summary>The history, summary and verification of the ledger for the Admin, the worker and the agency (ticket LEDGER-11).</summary>
public class LedgerHistoryTests
{
    private const int Worker = 7;
    private const int Agency = 3;

    // Vietnam is UTC+7, so a local day starts at 17:00 UTC of the day before.
    private static readonly DateTime OctoberLast = new(2026, 10, 31, 16, 59, 59, DateTimeKind.Utc); // 31 Oct 23:59:59
    private static readonly DateTime NovemberFirst = new(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc); // 1 Nov 00:00:00
    private static readonly DateTime NovemberMiddle = new(2026, 11, 5, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime NovemberLast = new(2026, 11, 30, 16, 59, 59, DateTimeKind.Utc); // 30 Nov 23:59:59
    private static readonly DateTime DecemberFirst = new(2026, 11, 30, 17, 0, 0, DateTimeKind.Utc); // 1 Dec 00:00:00

    private class Rig
    {
        public MemoryLedger Ledger { get; } = new();
        public MemoryHistoryQuery Query { get; }
        public MemoryWithdrawals Workers { get; } = new();
        public LedgerFlow Flow { get; } = new();

        public Rig() => Query = new MemoryHistoryQuery(Ledger);

        public LedgerHistoryService Service() => new(Query, Workers, new StubWorkerEarnings(), new LedgerChainVerifier(Ledger), Flow.Clock);

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
                EntryHash = new string('0', 64),
                PrevHash = new string('0', 64),
            };
            typeof(LedgerEntry).GetProperty(nameof(LedgerEntry.EntryCode))!.SetValue(row, $"LG{row.EntryId:00000000}");
            Ledger.Rows.Add(row);
            return row;
        }

        public static LedgerParty W(int id) => new(LedgerPartyType.Worker, id);
        public static LedgerParty A(int id) => new(LedgerPartyType.Agency, id);
        public static LedgerParty E(int id) => new(LedgerPartyType.AgencyEscrow, id);
        public LedgerEntry Earn(LedgerParty to, decimal amount, DateTime at) => Add(LedgerRules.Clearing, to, amount, LedgerEntryType.JobEarning, at);
    }

    private static async Task<T> Thrown<T>(Func<Task> action) where T : Exception => await Assert.ThrowsAsync<T>(action);

    private static LedgerHistoryRequest Req(string? from = null, string? to = null, string? type = null, int? page = null, int? pageSize = null, string? partyType = null, int? partyId = null) =>
        new(from, to, type, page, pageSize, partyType, partyId);

    // ---- Admin ----------------------------------------------------------------------------------

    [Fact]
    public async Task Admin_sees_every_entry_newest_first_with_the_fields_of_the_row()
    {
        var rig = new Rig();
        rig.Earn(Rig.W(Worker), 80_000m, NovemberMiddle);
        rig.Add(LedgerRules.Clearing, LedgerRules.Revenue, 20_000m, LedgerEntryType.Commission, NovemberMiddle);

        var page = await rig.Service().GetAdminHistoryAsync(Req());

        Assert.Equal((1, 20, 2), (page.Page, page.PageSize, page.Total));
        Assert.Equal(["LG00000002", "LG00000001"], page.Items.Select(i => i.EntryCode).ToArray());
        var first = page.Items[1];
        Assert.Equal(("JOB_EARNING", "PLATFORM_CLEARING", (int?)null, "WORKER", (int?)Worker, 80_000m, "ASSIGNMENT"),
            (first.EntryType, first.FromPartyType, first.FromPartyId, first.ToPartyType, first.ToPartyId, first.Amount, first.RefType));
        Assert.Equal(DateTimeKind.Utc, first.CreatedAt.Kind);
        Assert.Null(first.SignedAmount); // an Admin row has no viewpoint
    }

    [Fact]
    public async Task Admin_filters_by_type_and_by_party_exactly_or_by_type_of_party()
    {
        var rig = new Rig();
        rig.Earn(Rig.W(7), 80_000m, NovemberMiddle);
        rig.Earn(Rig.W(8), 40_000m, NovemberMiddle);
        rig.Earn(Rig.A(7), 10_000m, NovemberMiddle); // an agency with the same id as worker 7
        rig.Add(LedgerRules.Clearing, LedgerRules.Revenue, 20_000m, LedgerEntryType.Commission, NovemberMiddle);
        rig.Add(Rig.W(7), LedgerRules.Revenue, 5_000m, LedgerEntryType.Penalty, NovemberMiddle);
        var service = rig.Service();

        Assert.Equal(1, (await service.GetAdminHistoryAsync(Req(type: "commission"))).Total); // case does not matter
        Assert.Equal(3, (await service.GetAdminHistoryAsync(Req(type: "JOB_EARNING"))).Total);
        Assert.Equal([5_000m, 80_000m], (await service.GetAdminHistoryAsync(Req(partyType: "WORKER", partyId: 7))).Items.Select(i => i.Amount).ToArray()); // sent or received, not the agency
        Assert.Equal(3, (await service.GetAdminHistoryAsync(Req(partyType: "WORKER"))).Total); // any worker: 7 (two rows) and 8
        Assert.Equal(2, (await service.GetAdminHistoryAsync(Req(partyType: "PLATFORM_REVENUE"))).Total); // the platform's own account has no id
        Assert.Equal(1, (await service.GetAdminHistoryAsync(Req(type: "PENALTY", partyType: "WORKER", partyId: 7))).Total);
    }

    [Fact]
    public async Task The_days_are_Vietnam_days_both_ends_included_to_the_second()
    {
        var rig = new Rig();
        rig.Earn(Rig.W(Worker), 1_000m, OctoberLast);
        rig.Earn(Rig.W(Worker), 2_000m, NovemberFirst);
        rig.Earn(Rig.W(Worker), 3_000m, NovemberMiddle);
        rig.Earn(Rig.W(Worker), 4_000m, NovemberLast);
        rig.Earn(Rig.W(Worker), 5_000m, DecemberFirst);
        var service = rig.Service();

        var november = await service.GetAdminHistoryAsync(Req(from: "2026-11-01", to: "2026-11-30"));
        Assert.Equal([4_000m, 3_000m, 2_000m], november.Items.Select(i => i.Amount).ToArray());

        Assert.Equal([5_000m, 4_000m], (await service.GetAdminHistoryAsync(Req(from: "2026-11-30"))).Items.Select(i => i.Amount).ToArray()); // one bound only
        Assert.Equal([2_000m, 1_000m], (await service.GetAdminHistoryAsync(Req(to: "2026-11-01"))).Items.Select(i => i.Amount).ToArray());
        Assert.Equal(1, (await service.GetAdminHistoryAsync(Req(from: "2026-11-01", to: "2026-11-01"))).Total); // a single day
    }

    [Fact]
    public async Task Paging_has_a_total_and_bounds_of_1_to_100_with_a_default_of_20()
    {
        var rig = new Rig();
        for (var i = 0; i < 25; i++) rig.Earn(Rig.W(Worker), 1_000m + i, NovemberMiddle);
        var service = rig.Service();

        var first = await service.GetAdminHistoryAsync(Req());
        Assert.Equal((1, 20, 25, 20), (first.Page, first.PageSize, first.Total, first.Items.Count));
        var second = await service.GetAdminHistoryAsync(Req(page: 2));
        Assert.Equal(5, second.Items.Count);
        Assert.Empty((await service.GetAdminHistoryAsync(Req(page: 3))).Items);
        Assert.Equal(1, (await service.GetAdminHistoryAsync(Req(pageSize: 1))).Items.Count);
        Assert.Equal(25, (await service.GetAdminHistoryAsync(Req(pageSize: 100))).Items.Count);

        foreach (var bad in new[] { Req(page: 0), Req(pageSize: 0), Req(pageSize: 101), Req(page: -1) })
        {
            Assert.NotEmpty((await Thrown<ValidationException>(() => service.GetAdminHistoryAsync(bad))).Errors);
        }
    }

    [Theory]
    [InlineData("2026-13-01", null, null, null, null, "from")]
    [InlineData("01/11/2026", null, null, null, null, "from")]
    [InlineData(null, "tomorrow", null, null, null, "to")]
    [InlineData("2026-11-30", "2026-11-01", null, null, null, "from")]
    [InlineData(null, null, "SALARY", null, null, "type")]
    [InlineData(null, null, null, "BANK", null, "partyType")]
    [InlineData(null, null, null, null, 5, "partyId")]
    public async Task Bad_filters_are_a_400_with_the_field_named(string? from, string? to, string? type, string? partyType, int? partyId, string field)
    {
        var error = await Thrown<ValidationException>(() => new Rig().Service().GetAdminHistoryAsync(Req(from, to, type, null, null, partyType, partyId)));

        Assert.Contains(field, error.Errors.Keys);
    }

    // ---- Worker ---------------------------------------------------------------------------------

    private static Rig WorkerRig()
    {
        var rig = new Rig();
        rig.Earn(Rig.W(Worker), 1_000m, OctoberLast);
        rig.Earn(Rig.W(Worker), 2_000m, NovemberFirst);
        rig.Earn(Rig.W(Worker), 3_000m, NovemberMiddle);
        rig.Add(Rig.W(Worker), new LedgerParty(LedgerPartyType.External, null), 500m, LedgerEntryType.Withdrawal, NovemberLast);
        rig.Earn(Rig.W(Worker), 5_000m, DecemberFirst);
        rig.Earn(Rig.W(8), 777m, NovemberMiddle); // another worker
        rig.Earn(Rig.A(Worker), 888m, NovemberMiddle); // an agency with the same id
        return rig;
    }

    [Fact]
    public async Task A_worker_sees_only_their_own_rows_with_signed_amounts_and_the_balances_around_the_period()
    {
        var page = await WorkerRig().Service().GetWorkerHistoryAsync(Worker, Req(from: "2026-11-01", to: "2026-11-30"));

        Assert.Equal([-500m, 3_000m, 2_000m], page.Items.Select(i => i.SignedAmount!.Value).ToArray());
        Assert.All(page.Items, i => Assert.Equal("WORKER", i.Account));
        Assert.Equal((1_000m, 5_500m, 3), (page.OpeningBalance, page.ClosingBalance, page.Total)); // 1,000 before November; + 2,000 + 3,000 - 500 inside it
        Assert.Null(page.EscrowOpeningBalance);
    }

    [Fact]
    public async Task Without_bounds_the_opening_balance_is_zero_and_the_closing_balance_is_the_current_one_and_the_type_filter_never_changes_them()
    {
        var service = WorkerRig().Service();

        var all = await service.GetWorkerHistoryAsync(Worker, Req());
        Assert.Equal((0m, 10_500m, 5), (all.OpeningBalance, all.ClosingBalance, all.Total));

        var onlyWithdrawals = await service.GetWorkerHistoryAsync(Worker, Req(type: "WITHDRAWAL"));
        Assert.Equal((0m, 10_500m, 1), (onlyWithdrawals.OpeningBalance, onlyWithdrawals.ClosingBalance, onlyWithdrawals.Total));

        var untilMiddle = await service.GetWorkerHistoryAsync(Worker, Req(to: "2026-11-05"));
        Assert.Equal(6_000m, untilMiddle.ClosingBalance); // 1,000 + 2,000 + 3,000
    }

    [Fact]
    public async Task A_worker_never_sees_other_parties_whatever_the_filters()
    {
        var rig = WorkerRig();

        var page = await rig.Service().GetWorkerHistoryAsync(Worker, Req(pageSize: 100));

        Assert.DoesNotContain(page.Items, i => i.Amount is 777m or 888m);
        Assert.All(page.Items, i => Assert.True(
            (i.ToPartyType == "WORKER" && i.ToPartyId == Worker) || (i.FromPartyType == "WORKER" && i.FromPartyId == Worker)));
        rig.Workers.Workers[8] = new WithdrawalWorker(WorkerType.Freelancer, null, null);
        var other = await rig.Service().GetWorkerHistoryAsync(8, Req());
        Assert.Equal(777m, other.ClosingBalance); // only the money of worker 8
    }

    [Fact]
    public async Task Agency_staff_get_403_and_an_unknown_worker_404()
    {
        var rig = WorkerRig();
        rig.Workers.Workers[9] = new WithdrawalWorker(WorkerType.AgencyStaff, null, null);

        await Thrown<ForbiddenAccessException>(() => rig.Service().GetWorkerHistoryAsync(9, Req()));
        await Thrown<NotFoundException>(() => rig.Service().GetWorkerHistoryAsync(404, Req()));
    }

    // ---- Agency ---------------------------------------------------------------------------------

    [Fact]
    public async Task An_agency_sees_its_wallet_and_its_escrow_with_separate_balances_and_never_another_agency_or_a_worker_with_the_same_id()
    {
        var rig = new Rig();
        rig.Earn(Rig.A(Agency), 221_000m, OctoberLast);
        rig.Add(new LedgerParty(LedgerPartyType.External, Agency), Rig.E(Agency), 2_000_000m, LedgerEntryType.EscrowDeposit, NovemberFirst, LedgerRefType.EscrowTxn);
        rig.Add(Rig.E(Agency), LedgerRules.Revenue, 300_000m, LedgerEntryType.EscrowPenalty, NovemberMiddle, LedgerRefType.EscrowTxn);
        rig.Earn(Rig.A(Agency), 100_000m, NovemberMiddle);
        rig.Earn(Rig.A(4), 999_000m, NovemberMiddle); // another agency
        rig.Earn(Rig.W(Agency), 888_000m, NovemberMiddle); // a worker with the same id
        rig.Add(new LedgerParty(LedgerPartyType.External, 4), Rig.E(4), 5_000_000m, LedgerEntryType.EscrowDeposit, NovemberMiddle, LedgerRefType.EscrowTxn);

        var page = await rig.Service().GetAgencyHistoryAsync(Agency, Req(from: "2026-11-01", to: "2026-11-30"));

        Assert.Equal(3, page.Total);
        Assert.Equal([100_000m, -300_000m, 2_000_000m], page.Items.Select(i => i.SignedAmount!.Value).ToArray());
        Assert.Equal(["AGENCY", "AGENCY_ESCROW", "AGENCY_ESCROW"], page.Items.Select(i => i.Account).ToArray());
        Assert.Equal((221_000m, 321_000m), (page.OpeningBalance, page.ClosingBalance)); // the wallet
        Assert.Equal((0m, 1_700_000m), (page.EscrowOpeningBalance, page.EscrowClosingBalance)); // the escrow account
        Assert.DoesNotContain(page.Items, i => i.Amount is 999_000m or 888_000m or 5_000_000m);
    }

    // ---- summary and chain ----------------------------------------------------------------------

    [Fact]
    public async Task The_summary_is_the_net_balance_of_the_platform_accounts_the_workers_the_agencies_and_the_pending_withdrawals()
    {
        var rig = new Rig();
        rig.Add(new LedgerParty(LedgerPartyType.External, 12), LedgerRules.Clearing, 260_000m, LedgerEntryType.OrderPaid, NovemberMiddle);
        rig.Add(LedgerRules.Clearing, Rig.W(Worker), 150_000m, LedgerEntryType.JobEarning, NovemberMiddle);
        rig.Add(LedgerRules.Clearing, Rig.A(Agency), 30_000m, LedgerEntryType.JobEarning, NovemberMiddle);
        rig.Add(LedgerRules.Clearing, LedgerRules.Revenue, 80_000m, LedgerEntryType.Commission, NovemberMiddle);
        rig.Add(Rig.W(Worker), new LedgerParty(LedgerPartyType.External, null), 50_000m, LedgerEntryType.Withdrawal, NovemberMiddle);
        rig.Query.Held = 20_000m;

        var summary = await rig.Service().GetSummaryAsync();

        Assert.Equal((80_000m, 0m, 100_000m, 30_000m, 20_000m), (summary.Revenue, summary.Clearing, summary.WorkerDebt, summary.AgencyDebt, summary.Held));
    }

    [Fact]
    public async Task Verify_reports_an_intact_chain_and_the_first_broken_entry_after_a_row_was_changed()
    {
        var flow = new LedgerFlow();
        await flow.Writer.AppendAsync(
        [
            new NewLedgerEntry(LedgerRules.Customer(12), LedgerRules.Clearing, 260_000m, LedgerEntryType.OrderPaid, LedgerRefType.Order, 1, 1, null, "a"),
            new NewLedgerEntry(LedgerRules.Clearing, Rig.W(Worker), 208_000m, LedgerEntryType.JobEarning, LedgerRefType.Assignment, 1, 1, null, "b"),
            new NewLedgerEntry(LedgerRules.Clearing, LedgerRules.Revenue, 52_000m, LedgerEntryType.Commission, LedgerRefType.Assignment, 1, 1, null, "c"),
        ]);
        var service = new LedgerHistoryService(new MemoryHistoryQuery(flow.Ledger), new MemoryWithdrawals(), new StubWorkerEarnings(), new LedgerChainVerifier(flow.Ledger), flow.Clock);

        Assert.Equal((true, 3, (string?)null), ((await service.VerifyAsync()).Ok, (await service.VerifyAsync()).CheckedCount, (await service.VerifyAsync()).FirstBrokenEntryCode));

        flow.Ledger.Rows[1].Amount++;
        var broken = await service.VerifyAsync();
        Assert.Equal((false, "LG00000002"), (broken.Ok, broken.FirstBrokenEntryCode));
    }

    // ---- controllers ----------------------------------------------------------------------------

    private class StubHistory : ILedgerHistoryService
    {
        public LedgerHistoryRequest? LastRequest { get; private set; }
        public int? LastWorker { get; private set; }
        public int? LastAgency { get; private set; }
        public string? LastMonth { get; private set; }

        private static readonly PartyLedgerPageDto Page = new([], 0m, 0m, 1, 20, 0);

        public Task<AdminLedgerPageDto> GetAdminHistoryAsync(LedgerHistoryRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new AdminLedgerPageDto([], 1, 20, 0));
        }

        public Task<PartyLedgerPageDto> GetWorkerHistoryAsync(int workerId, LedgerHistoryRequest request, CancellationToken cancellationToken = default)
        {
            (LastWorker, LastRequest) = (workerId, request);
            return Task.FromResult(Page);
        }

        public Task<PartyLedgerPageDto> GetAgencyHistoryAsync(int agencyId, LedgerHistoryRequest request, CancellationToken cancellationToken = default)
        {
            (LastAgency, LastRequest) = (agencyId, request);
            return Task.FromResult(Page);
        }

        public Task<LedgerSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default) => Task.FromResult(new LedgerSummaryDto(1m, 2m, 3m, 4m, 5m));

        public Task<LedgerChainResult> VerifyAsync(CancellationToken cancellationToken = default) => Task.FromResult(new LedgerChainResult(true, 9, null));

        public Task<WorkerStatementDto> GetWorkerStatementAsync(int workerId, string? month, CancellationToken cancellationToken = default)
        {
            (LastWorker, LastMonth) = (workerId, month);
            return Task.FromResult(new WorkerStatementDto(month ?? "", 0m, [], 0m, 0m, 0m, 0m, null));
        }
    }

    private class StubUser(int? id) : ICurrentUser
    {
        public bool IsAuthenticated => id is not null;
        public int? UserId => id;
        public UserRole? Role => null;
    }

    private class StubReconciliation : ILedgerReconciliationService
    {
        public Task<LedgerReconciliationReport> ReportAsync(CancellationToken cancellationToken = default) => Task.FromResult(new LedgerReconciliationReport([]));
        public Task<LedgerSweepResult> SweepAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task The_admin_endpoints_pass_the_query_and_answer_the_envelope()
    {
        var history = new StubHistory();
        var controller = new AdminLedgerController(new StubReconciliation(), history, null!);

        Assert.IsType<OkObjectResult>(await controller.List("2026-11-01", "2026-11-30", "COMMISSION", "WORKER", 7, 2, 50, default));
        Assert.Equal(new LedgerHistoryRequest("2026-11-01", "2026-11-30", "COMMISSION", 2, 50, "WORKER", 7), history.LastRequest);
        var summary = Assert.IsType<ApiResponse<LedgerSummaryDto>>(Assert.IsType<OkObjectResult>(await controller.Summary(default)).Value);
        Assert.Equal(5m, summary.Data!.Held);
        var verify = Assert.IsType<ApiResponse<LedgerChainResult>>(Assert.IsType<OkObjectResult>(await controller.Verify(default)).Value);
        Assert.Equal((true, 9), (verify.Data!.Ok, verify.Data.CheckedCount));
    }

    [Fact]
    public async Task The_worker_and_agency_endpoints_take_the_caller_from_the_token_and_answer_401_without_one()
    {
        var history = new StubHistory();

        Assert.IsType<OkObjectResult>(await new WorkerWalletController(null!, history, new StubUser(Worker)).Ledger("2026-11-01", null, null, null, null, default));
        Assert.Equal(Worker, history.LastWorker);
        Assert.Equal("2026-11-01", history.LastRequest!.From);

        Assert.IsType<OkObjectResult>(await new AgencyLedgerController(history, null!, new StubUser(Agency)).Ledger(null, null, "SETTLEMENT", 1, 10, default));
        Assert.Equal(Agency, history.LastAgency);

        Assert.IsType<UnauthorizedObjectResult>(await new WorkerWalletController(null!, history, new StubUser(null)).Ledger(null, null, null, null, null, default));
        Assert.IsType<UnauthorizedObjectResult>(await new AgencyLedgerController(history, null!, new StubUser(null)).Ledger(null, null, null, null, null, default));
    }

    [Theory]
    [InlineData(typeof(AdminLedgerController), "AdminOnly", "api/v1/admin/ledger")]
    [InlineData(typeof(WorkerWalletController), "WorkerOnly", "api/v1/workers/me")]
    [InlineData(typeof(AgencyLedgerController), "PartnerOnly", "api/v1/agencies/me")]
    public void The_endpoints_are_on_their_contract_routes_and_policies(Type controller, string policy, string route)
    {
        Assert.Equal(policy, controller.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Policy);
        Assert.Equal(route, ((RouteAttribute)controller.GetCustomAttributes(typeof(RouteAttribute), true).Single()).Template);
    }
}
