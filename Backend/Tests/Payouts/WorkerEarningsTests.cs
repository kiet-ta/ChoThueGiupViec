using System.Reflection;
using CommonService.Application.Common.Models;
using CommonService.Application.Features.Ledger;
using CommonService.Application.Features.Payouts;
using CommonService.Application.Features.Payouts.Dtos;
using CommonService.Application.Features.Payouts.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Tests.Ledger;
using CommonService.WebAPI.Controllers.Payouts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace CommonService.Tests.Payouts;

/// <summary>
/// The freelancer's monthly income and payout history (contract payouts.md 2.5, decision Q11), read from the ledger since decision D28:
/// the figures are the settlement's, including the advance (withdrawals) and the carry-in from earlier months.
/// </summary>
public class WorkerEarningsTests
{
    // 2026-11-10 11:00 Asia/Ho_Chi_Minh: the current month is 2026-11.
    private static readonly DateTime Now = new(2026, 11, 10, 4, 0, 0, DateTimeKind.Utc);
    private const int WorkerId = 5;

    private class MemoryEarnings : IWorkerEarningsRepository
    {
        public WorkerType? Type { get; set; } = WorkerType.Freelancer;
        public WorkerBatchState? State { get; set; }
        public List<WorkerPayoutHistoryRow> History { get; } = [];
        public int? AskedHistoryFor { get; private set; }
        public int StateAsked { get; private set; }

        public Task<WorkerType?> GetWorkerTypeAsync(int workerId, CancellationToken cancellationToken = default) => Task.FromResult(Type);

        public Task<WorkerBatchState?> GetBatchStateAsync(int workerId, string periodMonth, CancellationToken cancellationToken = default)
        {
            StateAsked++;
            return Task.FromResult(State);
        }

        public Task<(IReadOnlyList<WorkerPayoutHistoryRow> Items, int Total)> GetClosedItemsAsync(int workerId, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            AskedHistoryFor = workerId;
            var all = History.OrderByDescending(h => h.PeriodMonth).ToList();
            return Task.FromResult<(IReadOnlyList<WorkerPayoutHistoryRow>, int)>((all.Skip((page - 1) * pageSize).Take(pageSize).ToList(), all.Count));
        }
    }

    private class Rig
    {
        public MemoryEarnings Repo { get; } = new();
        public MemoryLedger Ledger { get; } = new();
        public LedgerStoreQuery Query { get; }

        public Rig() => Query = new LedgerStoreQuery(Ledger);

        public WorkerEarningsService Service() => new(Repo, Query, new PayoutBatchServiceTests.TestClock(Now));

        private LedgerEntry Add(
            LedgerParty from, LedgerParty to, decimal amount, LedgerEntryType type, LedgerRefType refType, long refId, DateTime at, long? orderId = null)
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
                CreatedAt = at,
                EntryHash = new string('0', 64),
                PrevHash = new string('0', 64),
            };
            Ledger.Rows.Add(row);
            return row;
        }

        private static LedgerParty Worker(int id) => new(LedgerPartyType.Worker, id);

        /// <summary>A completed job: the worker's earning and the platform's commission (20 % unless told otherwise).</summary>
        public Rig Completed(long assignmentId, decimal gross, DateTime at, int worker = WorkerId, decimal rate = 0.20m)
        {
            var commission = Math.Round(gross * rate, 0, MidpointRounding.AwayFromZero);
            Add(LedgerRules.Clearing, Worker(worker), gross - commission, LedgerEntryType.JobEarning, LedgerRefType.Assignment, assignmentId, at, orderId: 1000 + assignmentId);
            Add(LedgerRules.Clearing, LedgerRules.Revenue, commission, LedgerEntryType.Commission, LedgerRefType.Assignment, assignmentId, at, orderId: 1000 + assignmentId);
            return this;
        }

        public Rig Extension(long extensionId, decimal gross, DateTime at, int worker = WorkerId)
        {
            var commission = Math.Round(gross * 0.20m, 0, MidpointRounding.AwayFromZero);
            Add(LedgerRules.Clearing, Worker(worker), gross - commission, LedgerEntryType.ExtensionEarning, LedgerRefType.Extension, extensionId, at);
            Add(LedgerRules.Clearing, LedgerRules.Revenue, commission, LedgerEntryType.Commission, LedgerRefType.Extension, extensionId, at);
            return this;
        }

        public Rig AbsenceFee(long assignmentId, decimal fee, DateTime at, int worker = WorkerId)
        {
            Add(LedgerRules.Clearing, Worker(worker), fee, LedgerEntryType.AbsenceFee, LedgerRefType.Assignment, assignmentId, at, orderId: 1000 + assignmentId);
            return this;
        }

        public Rig Withdrew(decimal amount, DateTime at, int worker = WorkerId)
        {
            Add(Worker(worker), new LedgerParty(LedgerPartyType.External, null), amount, LedgerEntryType.Withdrawal, LedgerRefType.Withdrawal, 1, at);
            return this;
        }

        public Rig Penalised(decimal amount, DateTime at, int worker = WorkerId)
        {
            Add(Worker(worker), LedgerRules.Revenue, amount, LedgerEntryType.Penalty, LedgerRefType.Dispute, 4, at);
            return this;
        }
    }

    private static DateTime Utc(int y, int m, int d, int h = 3) => new(y, m, d, h, 0, 0, DateTimeKind.Utc);

    // ---- who may read it, and which month -------------------------------------------------------

    [Fact]
    public async Task An_unknown_worker_is_a_404_and_an_agency_staff_member_is_a_403_and_nothing_of_the_ledger_is_read()
    {
        var rig = new Rig();
        rig.Repo.Type = null;
        Assert.Equal(404, (await rig.Service().GetEarningsAsync(WorkerId, null)).StatusCode);

        rig.Repo.Type = WorkerType.AgencyStaff;
        var staff = await rig.Service().GetEarningsAsync(WorkerId, null);
        Assert.Equal(403, staff.StatusCode);
        Assert.False(staff.Success);
        Assert.Contains("agency", staff.ErrorMessage);
        Assert.Equal(0, rig.Repo.StateAsked);
    }

    [Fact]
    public async Task The_month_defaults_to_the_current_month_in_Ho_Chi_Minh_and_is_read_in_that_time_zone()
    {
        var rig = new Rig()
            .Completed(1, 100_000m, new DateTime(2026, 10, 31, 16, 59, 59, DateTimeKind.Utc)) // 23:59:59 on 31 October: not November
            .Completed(2, 100_000m, new DateTime(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc)); // 00:00:00 on 1 November

        var result = await rig.Service().GetEarningsAsync(WorkerId, null);

        Assert.Equal("2026-11", result.Data!.PeriodMonth);
        Assert.Equal([2L], result.Data.Jobs.Select(j => j.AssignmentId).ToArray());
        Assert.Equal(80_000m, result.Data.CarryInAmount); // the job of October is the carry-in of November
    }

    [Theory]
    [InlineData("2026-12")]
    [InlineData("2027-01")]
    [InlineData("2026-13")]
    [InlineData("2026-1")]
    [InlineData("November")]
    public async Task A_future_or_malformed_month_is_a_400(string month)
    {
        var result = await new Rig().Service().GetEarningsAsync(WorkerId, month);

        Assert.Equal(400, result.StatusCode);
        Assert.Contains("month", result.ValidationErrors!.Keys);
    }

    [Theory]
    [InlineData("2026-11")] // the running month is allowed
    [InlineData(" 2026-10 ")]
    [InlineData("2025-01")]
    public async Task The_current_and_past_months_are_allowed(string month)
    {
        Assert.True((await new Rig().Service().GetEarningsAsync(WorkerId, month)).Success);
    }

    // ---- the numbers ----------------------------------------------------------------------------

    [Fact]
    public async Task The_month_adds_up_jobs_and_the_absence_fee_with_the_commission_only_on_jobs()
    {
        var rig = new Rig()
            .Completed(1, 260_000m, Utc(2026, 11, 3))
            .Completed(2, 100_001m, Utc(2026, 11, 5))
            .AbsenceFee(3, 104_000m, Utc(2026, 11, 7));

        var e = (await rig.Service().GetEarningsAsync(WorkerId, "2026-11")).Data!;

        Assert.Equal((3, 464_001m, 72_000m, 0m, 0m, 0m, 392_001m), (e.JobCount, e.GrossAmount, e.CommissionAmount, e.PenaltyAmount, e.AdvanceAmount, e.CarryInAmount, e.NetAmount));
        Assert.Equal([1L, 2L, 3L], e.Jobs.Select(j => j.AssignmentId).ToArray()); // oldest first
        Assert.Equal((260_000m, 52_000m, 208_000m, false), (e.Jobs[0].GrossAmount, e.Jobs[0].CommissionAmount, e.Jobs[0].NetAmount, e.Jobs[0].AbsenceFee));
        Assert.Equal((20_000m, 80_001m), (e.Jobs[1].CommissionAmount, e.Jobs[1].NetAmount)); // 20000.2 rounded, the worker's 80 %
        Assert.Equal((104_000m, 0m, 104_000m, true), (e.Jobs[2].GrossAmount, e.Jobs[2].CommissionAmount, e.Jobs[2].NetAmount, e.Jobs[2].AbsenceFee));
        Assert.Equal(1001L, e.Jobs[0].OrderId);
    }

    [Fact]
    public async Task The_worked_example_shows_the_advance_and_the_deduction_and_ends_at_138000()
    {
        var rig = new Rig()
            .Completed(10, 260_000m, Utc(2026, 10, 5))
            .Extension(5, 100_000m, Utc(2026, 10, 5))
            .Withdrew(100_000m, Utc(2026, 10, 12))
            .Penalised(50_000m, Utc(2026, 10, 14));

        var e = (await rig.Service().GetEarningsAsync(WorkerId, "2026-10")).Data!;

        Assert.Equal((1, 360_000m, 72_000m, 50_000m, 100_000m, 0m, 138_000m),
            (e.JobCount, e.GrossAmount, e.CommissionAmount, e.PenaltyAmount, e.AdvanceAmount, e.CarryInAmount, e.NetAmount));
        Assert.Equal([10L], e.Jobs.Select(j => j.AssignmentId).ToArray()); // the extension is in the totals, not a job of its own
        Assert.Equal("NOT_BUILT", e.PayoutStatus);
    }

    [Fact]
    public async Task A_debt_of_an_earlier_month_is_the_carry_in_and_is_paid_off_before_anything_is_due()
    {
        var rig = new Rig()
            .Completed(1, 100_000m, Utc(2026, 9, 3)) // 80,000
            .Penalised(150_000m, Utc(2026, 9, 10)) // -70,000 carried
            .Completed(2, 300_000m, Utc(2026, 10, 3)); // 240,000

        var e = (await rig.Service().GetEarningsAsync(WorkerId, "2026-10")).Data!;

        Assert.Equal((-70_000m, 240_000m, 170_000m), (e.CarryInAmount, e.GrossAmount - e.CommissionAmount, e.NetAmount));
    }

    [Fact]
    public async Task A_deduction_larger_than_the_month_leaves_a_net_of_zero_never_a_negative_one()
    {
        var rig = new Rig().Completed(1, 100_000m, Utc(2026, 10, 3)).Penalised(150_000m, Utc(2026, 10, 10));

        var e = (await rig.Service().GetEarningsAsync(WorkerId, "2026-10")).Data!;

        Assert.Equal((150_000m, 0m), (e.PenaltyAmount, e.NetAmount));
    }

    [Fact]
    public async Task A_pending_withdrawal_request_stays_in_the_wallet_and_is_not_part_of_the_net()
    {
        var rig = new Rig().Completed(1, 360_000m, Utc(2026, 10, 3));
        rig.Query.Held[WorkerId] = 100_000m;

        var e = (await rig.Service().GetEarningsAsync(WorkerId, "2026-10")).Data!;

        Assert.Equal(188_000m, e.NetAmount);
    }

    [Fact]
    public async Task A_month_without_work_is_all_zero_with_no_jobs_and_NOT_BUILT()
    {
        var e = (await new Rig().Service().GetEarningsAsync(WorkerId, "2026-10")).Data!;

        Assert.Equal((0, 0m, 0m, 0m, 0m, 0m, 0m, "NOT_BUILT"),
            (e.JobCount, e.GrossAmount, e.CommissionAmount, e.PenaltyAmount, e.AdvanceAmount, e.CarryInAmount, e.NetAmount, e.PayoutStatus));
        Assert.Empty(e.Jobs);
    }

    [Fact]
    public async Task The_money_of_other_workers_and_of_agencies_never_reaches_this_worker()
    {
        var rig = new Rig().Completed(1, 260_000m, Utc(2026, 10, 3)).Completed(2, 999_000m, Utc(2026, 10, 4), worker: 6).Withdrew(500_000m, Utc(2026, 10, 5), worker: 6);
        rig.Ledger.Rows.Add(new LedgerEntry
        {
            EntryId = 99,
            FromPartyType = LedgerPartyType.PlatformClearing,
            ToPartyType = LedgerPartyType.Agency,
            ToPartyId = WorkerId,
            Amount = 777_000m,
            EntryType = LedgerEntryType.JobEarning,
            RefType = LedgerRefType.Assignment,
            RefId = 77,
            CreatedAt = Utc(2026, 10, 6),
            IdempotencyKey = "agency-5",
        }); // an agency that happens to have the same id

        var e = (await rig.Service().GetEarningsAsync(WorkerId, "2026-10")).Data!;

        Assert.Equal((1, 208_000m, 0m), (e.JobCount, e.NetAmount, e.AdvanceAmount));
        Assert.Equal([1L], e.Jobs.Select(j => j.AssignmentId).ToArray());
    }

    // ---- payout status and the closed batch -----------------------------------------------------

    [Theory]
    [InlineData(null, "NOT_BUILT")]
    [InlineData("DRAFT", "PENDING")]
    [InlineData("CLOSED", "TRANSFERRED")]
    public async Task The_payout_status_follows_the_batch_of_the_month(string? batchStatus, string expected)
    {
        var rig = new Rig().Completed(1, 260_000m, Utc(2026, 10, 3));
        rig.Repo.State = batchStatus is null ? null : new WorkerBatchState(9, batchStatus, null);

        var e = (await rig.Service().GetEarningsAsync(WorkerId, "2026-10")).Data!;

        Assert.Equal(expected, e.PayoutStatus);
        Assert.Equal(208_000m, e.NetAmount); // no item for the worker: the numbers of the ledger
    }

    [Fact]
    public async Task Once_the_batch_is_CLOSED_the_stored_item_is_the_answer_even_when_the_ledger_says_otherwise()
    {
        var rig = new Rig().Completed(1, 260_000m, Utc(2026, 10, 3));
        rig.Repo.State = new WorkerBatchState(9, "CLOSED", new WorkerBatchItem(4, 900_000m, 180_000m, 20_000m, 150_000m, 30_000m, 580_000m));

        var e = (await rig.Service().GetEarningsAsync(WorkerId, "2026-10")).Data!;

        Assert.Equal("TRANSFERRED", e.PayoutStatus);
        Assert.Equal((4, 900_000m, 180_000m, 20_000m, 150_000m, 30_000m, 580_000m),
            (e.JobCount, e.GrossAmount, e.CommissionAmount, e.PenaltyAmount, e.AdvanceAmount, e.CarryInAmount, e.NetAmount));
    }

    [Fact]
    public async Task A_DRAFT_item_is_not_trusted_because_it_may_be_stale_the_month_is_recomputed()
    {
        var rig = new Rig().Completed(1, 260_000m, Utc(2026, 10, 3)).Completed(2, 260_000m, Utc(2026, 10, 4));
        rig.Repo.State = new WorkerBatchState(9, "DRAFT", new WorkerBatchItem(1, 260_000m, 52_000m, 0m, 0m, 0m, 208_000m)); // made before job 2

        var e = (await rig.Service().GetEarningsAsync(WorkerId, "2026-10")).Data!;

        Assert.Equal("PENDING", e.PayoutStatus);
        Assert.Equal((2, 416_000m), (e.JobCount, e.NetAmount));
    }

    // ---- payout history -------------------------------------------------------------------------

    [Fact]
    public async Task The_history_lists_the_workers_closed_items_with_advance_and_carry_in_newest_month_first_and_asks_only_for_this_worker()
    {
        var rig = new Rig();
        rig.Repo.History.Add(new WorkerPayoutHistoryRow(1, "2026-08", 0m, 0m, 100_000m, "TRANSFERRED", new DateTime(2026, 9, 2, 3, 0, 0)));
        rig.Repo.History.Add(new WorkerPayoutHistoryRow(2, "2026-10", 100_000m, -20_000m, 300_000m, "TRANSFERRED", new DateTime(2026, 11, 2, 3, 0, 0)));
        rig.Repo.History.Add(new WorkerPayoutHistoryRow(3, "2026-09", 0m, 0m, 200_000m, "TRANSFERRED", null));

        var result = await rig.Service().GetPayoutsAsync(WorkerId, null, null);

        var page = result.Data!;
        Assert.Equal(["2026-10", "2026-09", "2026-08"], page.Items.Select(i => i.PeriodMonth).ToArray());
        Assert.Equal((1, 20, 3), (page.Page, page.PageSize, page.Total));
        Assert.Equal((2, 100_000m, -20_000m, 300_000m, "TRANSFERRED"),
            (page.Items[0].BatchId, page.Items[0].AdvanceAmount, page.Items[0].CarryInAmount, page.Items[0].NetAmount, page.Items[0].ItemStatus));
        Assert.Equal(DateTimeKind.Utc, page.Items[0].TransferredAt!.Value.Kind);
        Assert.Null(page.Items[1].TransferredAt);
        Assert.Equal(WorkerId, rig.Repo.AskedHistoryFor);
    }

    [Fact]
    public async Task The_history_is_paged_and_an_agency_staff_member_simply_has_nothing()
    {
        var rig = new Rig();
        for (var i = 1; i <= 5; i++) rig.Repo.History.Add(new WorkerPayoutHistoryRow(i, $"2026-0{i}", 0m, 0m, 1000m * i, "TRANSFERRED", null));

        var second = (await rig.Service().GetPayoutsAsync(WorkerId, "2", "2")).Data!;
        Assert.Equal(["2026-03", "2026-02"], second.Items.Select(i => i.PeriodMonth).ToArray());
        Assert.Equal(5, second.Total);

        rig.Repo.History.Clear();
        rig.Repo.Type = WorkerType.AgencyStaff;
        var staff = await rig.Service().GetPayoutsAsync(WorkerId, null, null);
        Assert.True(staff.Success);
        Assert.Empty(staff.Data!.Items);
    }

    [Theory]
    [InlineData("0", null, "page")]
    [InlineData("x", null, "page")]
    [InlineData(null, "0", "pageSize")]
    [InlineData(null, "101", "pageSize")]
    public async Task Bad_history_paging_is_a_400(string? page, string? pageSize, string field)
    {
        var result = await new Rig().Service().GetPayoutsAsync(WorkerId, page, pageSize);

        Assert.Equal(400, result.StatusCode);
        Assert.Contains(field, result.ValidationErrors!.Keys);
    }

    // ---- controller -----------------------------------------------------------------------------

    private class FakeUser(int? id) : ICurrentUser
    {
        public bool IsAuthenticated => id is not null;
        public int? UserId => id;
        public UserRole? Role => null;
    }

    private class StubService(int status) : IWorkerEarningsService
    {
        public (int Worker, string? Month)? LastEarnings { get; private set; }
        public int? LastPayoutsFor { get; private set; }

        public Task<PayoutResult<WorkerEarningsDto>> GetEarningsAsync(int workerId, string? month, CancellationToken cancellationToken = default)
        {
            LastEarnings = (workerId, month);
            return Task.FromResult(status switch
            {
                200 => PayoutResult<WorkerEarningsDto>.Ok(new WorkerEarningsDto { PeriodMonth = "2026-10" }),
                400 => PayoutResult<WorkerEarningsDto>.ValidationError(new Dictionary<string, string[]> { ["month"] = ["bad"] }),
                403 => new PayoutResult<WorkerEarningsDto> { StatusCode = 403, ErrorMessage = "agency" },
                _ => PayoutResult<WorkerEarningsDto>.NotFound(),
            });
        }

        public Task<PayoutResult<WorkerPayoutHistoryPageDto>> GetPayoutsAsync(int workerId, string? page, string? pageSize, CancellationToken cancellationToken = default)
        {
            LastPayoutsFor = workerId;
            return Task.FromResult(PayoutResult<WorkerPayoutHistoryPageDto>.Ok(new WorkerPayoutHistoryPageDto()));
        }
    }

    [Fact]
    public void The_controller_is_worker_only_with_the_contract_routes()
    {
        var type = typeof(WorkerEarningsController);
        Assert.Equal("WorkerOnly", type.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal("api/v1/workers/me", type.GetCustomAttribute<RouteAttribute>()!.Template);

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.All(methods, m => Assert.Null(m.GetCustomAttribute<AllowAnonymousAttribute>()));
        var routes = methods
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>().Select(a => $"{a.HttpMethods.Single()} {a.Template}".Trim()))
            .Order().ToArray();
        Assert.Equal(["GET earnings", "GET payouts"], routes);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(404)]
    public async Task Earnings_maps_the_status_and_takes_the_worker_id_from_the_token_not_from_the_query(int status)
    {
        var service = new StubService(status);

        var result = Assert.IsType<ObjectResult>(await new WorkerEarningsController(service, new FakeUser(7)).Earnings("2026-10", default));

        Assert.Equal(status, result.StatusCode);
        Assert.Equal((7, "2026-10"), service.LastEarnings);
        if (status == 400) Assert.Contains("errors", Assert.IsType<ApiResponse<object>>(result.Value).Data!.GetType().GetProperties().Select(p => p.Name));
    }

    [Fact]
    public async Task Payouts_uses_the_token_worker_and_without_a_user_id_both_actions_answer_401()
    {
        var service = new StubService(200);

        Assert.IsType<ObjectResult>(await new WorkerEarningsController(service, new FakeUser(7)).Payouts(null, null, default));
        Assert.Equal(7, service.LastPayoutsFor);

        var anonymous = new WorkerEarningsController(new StubService(200), new FakeUser(null));
        Assert.IsType<UnauthorizedObjectResult>(await anonymous.Earnings(null, default));
        Assert.IsType<UnauthorizedObjectResult>(await anonymous.Payouts(null, null, default));
    }
}
