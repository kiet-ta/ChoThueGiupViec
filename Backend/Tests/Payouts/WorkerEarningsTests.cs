using System.Reflection;
using CommonService.Application.Common.Models;
using CommonService.Application.Features.Payouts;
using CommonService.Application.Features.Payouts.Dtos;
using CommonService.Application.Features.Payouts.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Modules.Payouts;
using CommonService.Infrastructure.Persistence;
using CommonService.WebAPI.Controllers.Payouts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace CommonService.Tests.Payouts;

/// <summary>BE-M6-07: the freelancer's monthly income and payout history (contract payouts.md 2.5, decision Q11).</summary>
public class WorkerEarningsTests
{
    // 2026-11-10 11:00 Asia/Ho_Chi_Minh: the current month is 2026-11.
    private static readonly DateTime Now = new(2026, 11, 10, 4, 0, 0, DateTimeKind.Utc);
    private const int WorkerId = 5;

    private sealed class MemoryEarnings : IWorkerEarningsRepository
    {
        public WorkerType? Type { get; set; } = WorkerType.Freelancer;
        public List<WorkerEarningRow> Rows { get; } = [];
        public WorkerBatchState? State { get; set; }
        public decimal Applied { get; set; }
        public List<WorkerPayoutHistoryRow> History { get; } = [];
        public (int Worker, DateTime From, DateTime To)? AskedRange { get; private set; }
        public int? AskedHistoryFor { get; private set; }
        public string? AskedAppliedBefore { get; private set; }

        public Task<WorkerType?> GetWorkerTypeAsync(int workerId, CancellationToken cancellationToken = default) => Task.FromResult(Type);

        public Task<IReadOnlyList<WorkerEarningRow>> GetEarningsAsync(int workerId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
        {
            AskedRange = (workerId, fromUtc, toUtc);
            return Task.FromResult<IReadOnlyList<WorkerEarningRow>>(Rows.ToList());
        }

        public Task<WorkerBatchState?> GetBatchStateAsync(int workerId, string periodMonth, CancellationToken cancellationToken = default) => Task.FromResult(State);

        public Task<decimal> GetAppliedPenaltyBeforeAsync(int workerId, string periodMonth, CancellationToken cancellationToken = default)
        {
            AskedAppliedBefore = periodMonth;
            return Task.FromResult(Applied);
        }

        public Task<(IReadOnlyList<WorkerPayoutHistoryRow> Items, int Total)> GetClosedItemsAsync(int workerId, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            AskedHistoryFor = workerId;
            var all = History.OrderByDescending(h => h.PeriodMonth).ToList();
            return Task.FromResult<(IReadOnlyList<WorkerPayoutHistoryRow>, int)>((all.Skip((page - 1) * pageSize).Take(pageSize).ToList(), all.Count));
        }
    }

    private sealed class StubPenalties : IPayoutPenaltySource
    {
        public Dictionary<PayeeKey, decimal> Decided { get; } = [];
        public DateTime? AskedThrough { get; private set; }

        public Task<IReadOnlyDictionary<PayeeKey, decimal>> GetDecidedAsync(DateTime throughUtc, CancellationToken cancellationToken = default)
        {
            AskedThrough = throughUtc;
            return Task.FromResult<IReadOnlyDictionary<PayeeKey, decimal>>(Decided);
        }
    }

    private sealed class Rig
    {
        public MemoryEarnings Repo { get; } = new();
        public StubPenalties Penalties { get; } = new();

        public WorkerEarningsService Service() => new(Repo, Penalties, new PayoutBatchServiceTests.TestClock(Now));

        public Rig Completed(long id, decimal gross, DateTime dateUtc, decimal rate = 0.200m)
        {
            Repo.Rows.Add(new WorkerEarningRow(id, 1000 + id, JobAssignmentStatus.Completed, gross, rate, null, dateUtc));
            return this;
        }

        public Rig AbsenceFee(long id, decimal fee, DateTime dateUtc)
        {
            Repo.Rows.Add(new WorkerEarningRow(id, 1000 + id, JobAssignmentStatus.Absent, 260000m, 0.200m, fee, dateUtc));
            return this;
        }
    }

    private static DateTime Utc(int y, int m, int d, int h = 3) => new(y, m, d, h, 0, 0, DateTimeKind.Utc);

    // ---- who may read it, and which month -------------------------------------------------------

    [Fact]
    public async Task An_unknown_worker_is_a_404_and_an_agency_staff_member_is_a_403()
    {
        var rig = new Rig();
        rig.Repo.Type = null;
        Assert.Equal(404, (await rig.Service().GetEarningsAsync(WorkerId, null)).StatusCode);

        rig.Repo.Type = WorkerType.AgencyStaff;
        var staff = await rig.Service().GetEarningsAsync(WorkerId, null);
        Assert.Equal(403, staff.StatusCode);
        Assert.False(staff.Success);
        Assert.Contains("agency", staff.ErrorMessage);
        Assert.Null(rig.Repo.AskedRange); // nothing about the money is read for them
    }

    [Fact]
    public async Task The_month_defaults_to_the_current_month_in_Ho_Chi_Minh_and_is_read_in_that_time_zone()
    {
        var rig = new Rig();

        var result = await rig.Service().GetEarningsAsync(WorkerId, null);

        Assert.Equal("2026-11", result.Data!.PeriodMonth);
        var range = rig.Repo.AskedRange!.Value;
        Assert.Equal(WorkerId, range.Worker);
        Assert.Equal(new DateTime(2026, 10, 31, 17, 0, 0), range.From); // 2026-11-01 00:00 +07:00
        Assert.Equal(new DateTime(2026, 11, 30, 17, 0, 0), range.To); // 2026-12-01 00:00 +07:00
        Assert.Equal(DateTimeKind.Utc, range.From.Kind);
        Assert.Equal(range.To, rig.Penalties.AskedThrough);
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
            .Completed(1, 260000m, Utc(2026, 11, 3))
            .Completed(2, 100001m, Utc(2026, 11, 5))
            .AbsenceFee(3, 104000m, Utc(2026, 11, 7));

        var result = await rig.Service().GetEarningsAsync(WorkerId, "2026-11");

        var e = result.Data!;
        Assert.Equal(3, e.JobCount);
        Assert.Equal(464001m, e.GrossAmount); // 260000 + 100001 + the 104000 fee
        Assert.Equal(72000m, e.CommissionAmount); // 52000 + 20000 (20000.2 rounded); none on the fee
        Assert.Equal(0m, e.PenaltyAmount);
        Assert.Equal(392001m, e.NetAmount);

        Assert.Equal([1L, 2L, 3L], e.Jobs.Select(j => j.AssignmentId).ToArray());
        var fee = e.Jobs[2];
        Assert.True(fee.AbsenceFee);
        Assert.Equal((104000m, 0m, 104000m), (fee.GrossAmount, fee.CommissionAmount, fee.NetAmount));
        var job = e.Jobs[1];
        Assert.False(job.AbsenceFee);
        Assert.Equal((100001m, 20000m, 80001m), (job.GrossAmount, job.CommissionAmount, job.NetAmount));
        Assert.Equal(1002L, job.OrderId);
        Assert.Equal(DateTimeKind.Utc, job.CompletedAt.Kind);
        Assert.Equal(Utc(2026, 11, 5), job.CompletedAt);
    }

    [Fact]
    public async Task The_rate_frozen_on_each_assignment_is_used()
    {
        var rig = new Rig().Completed(1, 100000m, Utc(2026, 11, 3), rate: 0.10m);

        var e = (await rig.Service().GetEarningsAsync(WorkerId, "2026-11")).Data!;

        Assert.Equal(10000m, e.CommissionAmount);
        Assert.Equal(90000m, e.NetAmount);
    }

    [Fact]
    public async Task A_month_without_work_is_all_zero_with_no_jobs_and_NOT_BUILT()
    {
        var e = (await new Rig().Service().GetEarningsAsync(WorkerId, "2026-10")).Data!;

        Assert.Equal((0, 0m, 0m, 0m, 0m), (e.JobCount, e.GrossAmount, e.CommissionAmount, e.PenaltyAmount, e.NetAmount));
        Assert.Empty(e.Jobs);
        Assert.Equal("NOT_BUILT", e.PayoutStatus);
    }

    [Fact]
    public async Task A_pending_penalty_is_deducted_as_in_the_batch_decided_minus_applied_capped_by_the_month()
    {
        var rig = new Rig().Completed(1, 260000m, Utc(2026, 10, 3)); // payable 208000
        rig.Penalties.Decided[new PayeeKey(PayeeType.Freelancer, WorkerId)] = 300000m;
        rig.Repo.Applied = 50000m;

        var e = (await rig.Service().GetEarningsAsync(WorkerId, "2026-10")).Data!;

        Assert.Equal(208000m, e.PenaltyAmount); // 250000 pending, only 208000 can be taken this month
        Assert.Equal(0m, e.NetAmount);
        Assert.Equal("2026-10", rig.Repo.AskedAppliedBefore);
    }

    [Fact]
    public async Task A_smaller_penalty_is_taken_in_full()
    {
        var rig = new Rig().Completed(1, 260000m, Utc(2026, 10, 3));
        rig.Penalties.Decided[new PayeeKey(PayeeType.Freelancer, WorkerId)] = 50000m;

        var e = (await rig.Service().GetEarningsAsync(WorkerId, "2026-10")).Data!;

        Assert.Equal(50000m, e.PenaltyAmount);
        Assert.Equal(158000m, e.NetAmount);
    }

    [Fact]
    public async Task Penalties_of_other_payees_never_reach_this_worker()
    {
        var rig = new Rig().Completed(1, 260000m, Utc(2026, 10, 3));
        rig.Penalties.Decided[new PayeeKey(PayeeType.Freelancer, WorkerId + 1)] = 100000m;
        rig.Penalties.Decided[new PayeeKey(PayeeType.Agency, WorkerId)] = 100000m; // an agency with the same id is another payee

        var e = (await rig.Service().GetEarningsAsync(WorkerId, "2026-10")).Data!;

        Assert.Equal(0m, e.PenaltyAmount);
        Assert.Equal(208000m, e.NetAmount);
    }

    // ---- payout status and the closed batch -----------------------------------------------------

    [Theory]
    [InlineData(null, "NOT_BUILT")]
    [InlineData("DRAFT", "PENDING")]
    [InlineData("CLOSED", "TRANSFERRED")]
    public async Task The_payout_status_follows_the_batch_of_the_month(string? batchStatus, string expected)
    {
        var rig = new Rig().Completed(1, 260000m, Utc(2026, 10, 3));
        rig.Repo.State = batchStatus is null ? null : new WorkerBatchState(9, batchStatus, null);

        var e = (await rig.Service().GetEarningsAsync(WorkerId, "2026-10")).Data!;

        Assert.Equal(expected, e.PayoutStatus);
        Assert.Equal(208000m, e.NetAmount); // no item for the worker: the computed numbers
    }

    [Fact]
    public async Task Once_the_batch_is_CLOSED_the_stored_item_is_the_answer_even_when_the_data_says_otherwise()
    {
        var rig = new Rig().Completed(1, 260000m, Utc(2026, 10, 3));
        rig.Repo.State = new WorkerBatchState(9, "CLOSED", new WorkerBatchItem(4, 900000m, 180000m, 20000m, 700000m));

        var e = (await rig.Service().GetEarningsAsync(WorkerId, "2026-10")).Data!;

        Assert.Equal("TRANSFERRED", e.PayoutStatus);
        Assert.Equal((4, 900000m, 180000m, 20000m, 700000m), (e.JobCount, e.GrossAmount, e.CommissionAmount, e.PenaltyAmount, e.NetAmount));
    }

    [Fact]
    public async Task A_DRAFT_item_is_not_trusted_because_it_may_be_stale_the_month_is_recomputed()
    {
        var rig = new Rig().Completed(1, 260000m, Utc(2026, 10, 3)).Completed(2, 260000m, Utc(2026, 10, 4));
        rig.Repo.State = new WorkerBatchState(9, "DRAFT", new WorkerBatchItem(1, 260000m, 52000m, 0m, 208000m)); // built before job 2

        var e = (await rig.Service().GetEarningsAsync(WorkerId, "2026-10")).Data!;

        Assert.Equal("PENDING", e.PayoutStatus);
        Assert.Equal(2, e.JobCount);
        Assert.Equal(416000m, e.NetAmount);
    }

    // ---- payout history -------------------------------------------------------------------------

    [Fact]
    public async Task The_history_lists_the_workers_closed_items_newest_month_first_and_asks_only_for_this_worker()
    {
        var rig = new Rig();
        rig.Repo.History.Add(new WorkerPayoutHistoryRow(1, "2026-08", 100000m, "TRANSFERRED", new DateTime(2026, 9, 2, 3, 0, 0)));
        rig.Repo.History.Add(new WorkerPayoutHistoryRow(2, "2026-10", 300000m, "TRANSFERRED", new DateTime(2026, 11, 2, 3, 0, 0)));
        rig.Repo.History.Add(new WorkerPayoutHistoryRow(3, "2026-09", 200000m, "TRANSFERRED", null));

        var result = await rig.Service().GetPayoutsAsync(WorkerId, null, null);

        var page = result.Data!;
        Assert.Equal(["2026-10", "2026-09", "2026-08"], page.Items.Select(i => i.PeriodMonth).ToArray());
        Assert.Equal((1, 20, 3), (page.Page, page.PageSize, page.Total));
        Assert.Equal((2, 300000m, "TRANSFERRED"), (page.Items[0].BatchId, page.Items[0].NetAmount, page.Items[0].ItemStatus));
        Assert.Equal(DateTimeKind.Utc, page.Items[0].TransferredAt!.Value.Kind);
        Assert.Null(page.Items[1].TransferredAt);
        Assert.Equal(WorkerId, rig.Repo.AskedHistoryFor);
    }

    [Fact]
    public async Task The_history_is_paged_and_an_agency_staff_member_simply_has_nothing()
    {
        var rig = new Rig();
        for (var i = 1; i <= 5; i++) rig.Repo.History.Add(new WorkerPayoutHistoryRow(i, $"2026-0{i}", 1000m * i, "TRANSFERRED", null));

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

    private sealed class FakeUser(int? id) : ICurrentUser
    {
        public bool IsAuthenticated => id is not null;
        public int? UserId => id;
        public UserRole? Role => null;
    }

    private sealed class StubService(int status) : IWorkerEarningsService
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
        Assert.Equal("api/workers/me", type.GetCustomAttribute<RouteAttribute>()!.Template);

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

    // ---- SQL Server -----------------------------------------------------------------------------

    private static WorkerEarningsService RealEarnings(AppDbContext db) =>
        new(new EfWorkerEarningsRepository(db), new CommonService.Infrastructure.Modules.Disputes.EfPayoutPenaltySource(db),
            new PayoutBatchServiceTests.TestClock(new DateTime(2099, 6, 1, 4, 0, 0, DateTimeKind.Utc)));

    [Fact]
    public async Task Real_database_a_worker_sees_their_own_month_PENDING_then_TRANSFERRED_and_never_another_workers_money()
    {
        if (!PayoutBatchDatabaseTests.IsSqlServerAvailable()) return;

        var seed = await PayoutBatchDatabaseTests.SeedBaseAsync(withAgency: true);
        const string period = "2091-02";
        try
        {
            var w1 = await PayoutBatchDatabaseTests.AddWorkerAsync(seed, "Freelancer One", "111");
            var w2 = await PayoutBatchDatabaseTests.AddWorkerAsync(seed, "Freelancer Two", "222");
            var staff = await PayoutBatchDatabaseTests.AddWorkerAsync(seed, "Agency Staff", "333", agencyStaff: true);

            // 2091-01-31T17:00Z is 2091-02-01 00:00 in Ho Chi Minh: the first instant of February.
            await PayoutBatchDatabaseTests.AddAssignmentAsync(seed, w1, PayoutBatchDatabaseTests.Utc(2091, 1, 31, 16, 59, 59)); // January: not shown
            var first = await PayoutBatchDatabaseTests.AddAssignmentAsync(seed, w1, PayoutBatchDatabaseTests.Utc(2091, 1, 31, 17, 0, 0));
            var second = await PayoutBatchDatabaseTests.AddAssignmentAsync(seed, w1, PayoutBatchDatabaseTests.Utc(2091, 2, 10, 3), gross: 100001m);
            var fee = await PayoutBatchDatabaseTests.AddAssignmentAsync(seed, w1, PayoutBatchDatabaseTests.Utc(2091, 2, 12, 3), absenceFee: 104000m);
            await PayoutBatchDatabaseTests.AddAssignmentAsync(seed, w1, PayoutBatchDatabaseTests.Utc(2091, 2, 28, 17, 0, 0)); // March: not shown
            await PayoutBatchDatabaseTests.AddAssignmentAsync(seed, w2, PayoutBatchDatabaseTests.Utc(2091, 2, 11, 3), gross: 999000m); // someone else's money
            await PayoutBatchDatabaseTests.AddAssignmentAsync(seed, staff, PayoutBatchDatabaseTests.Utc(2091, 2, 13, 3), agencyId: seed.AgencyId);

            await using (var db = new AppDbContext(PayoutBatchDatabaseTests.Options()))
            {
                var service = RealEarnings(db);
                var before = (await service.GetEarningsAsync(w1, period)).Data!;

                Assert.Equal("NOT_BUILT", before.PayoutStatus);
                Assert.Equal(3, before.JobCount);
                Assert.Equal(260000m + 100001m + 104000m, before.GrossAmount);
                Assert.Equal(52000m + 20000m, before.CommissionAmount);
                Assert.Equal(392001m, before.NetAmount);
                Assert.Equal([first, second, fee], before.Jobs.Select(j => j.AssignmentId).ToArray()); // oldest first, only mine
                Assert.True(before.Jobs[2].AbsenceFee);

                // the other freelancer's screen shows only their own job
                var other = (await service.GetEarningsAsync(w2, period)).Data!;
                Assert.Equal(1, other.JobCount);
                Assert.Equal(999000m - 199800m, other.NetAmount);

                var forStaff = await service.GetEarningsAsync(staff, period);
                Assert.Equal(403, forStaff.StatusCode); // the agency is paid, not the worker
            }

            int batchId;
            await using (var db = new AppDbContext(PayoutBatchDatabaseTests.Options()))
            {
                batchId = (await PayoutBatchDatabaseTests.Real(db).BuildAsync(period)).Data!.BatchId;
            }

            await using (var db = new AppDbContext(PayoutBatchDatabaseTests.Options()))
            {
                var service = RealEarnings(db);
                var draft = (await service.GetEarningsAsync(w1, period)).Data!;
                Assert.Equal("PENDING", draft.PayoutStatus);
                Assert.Equal(392001m, draft.NetAmount);
                Assert.Equal(3, draft.Jobs.Count); // assignments already attached to the DRAFT items are still listed

                Assert.Empty((await service.GetPayoutsAsync(w1, null, null)).Data!.Items); // nothing is closed yet
            }

            await using (var db = new AppDbContext(PayoutBatchDatabaseTests.Options()))
            {
                Assert.True((await PayoutBatchDatabaseTests.Real(db).ConfirmAsync(seed.AdminId, batchId)).Success);
            }

            await using (var db = new AppDbContext(PayoutBatchDatabaseTests.Options()))
            {
                var service = RealEarnings(db);
                var closed = (await service.GetEarningsAsync(w1, period)).Data!;
                Assert.Equal("TRANSFERRED", closed.PayoutStatus);
                Assert.Equal((3, 464001m, 72000m, 0m, 392001m), (closed.JobCount, closed.GrossAmount, closed.CommissionAmount, closed.PenaltyAmount, closed.NetAmount));

                var history = (await service.GetPayoutsAsync(w1, null, null)).Data!;
                var item = Assert.Single(history.Items);
                Assert.Equal((batchId, period, 392001m, "TRANSFERRED"), (item.BatchId, item.PeriodMonth, item.NetAmount, item.ItemStatus));
                Assert.NotNull(item.TransferredAt);
                Assert.Equal(1, history.Total);

                var staffHistory = (await service.GetPayoutsAsync(staff, null, null)).Data!;
                Assert.Empty(staffHistory.Items); // agency staff have no items of their own
            }
        }
        finally
        {
            await PayoutBatchDatabaseTests.CleanAsync(seed, period);
        }
    }
}
