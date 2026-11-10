using CommonService.Application.Exceptions;
using CommonService.Application.Features.Ledger;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.WebAPI.Controllers.Ledger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.Tests.Ledger;

/// <summary>The freelancer's wallet and advance requests (ticket LEDGER-07, decision E).</summary>
public class WithdrawalServiceTests
{
    private const int Worker = 7;
    private static readonly DateTime Now = new(2026, 11, 10, 4, 0, 0, DateTimeKind.Utc);

    private class Setup
    {
        public MemoryWithdrawals Repository { get; } = new();
        public FakeLedgerQuery Ledger { get; } = new();
        public FakeOpenDisputeQuery Disputes { get; } = new();
        public FakeClock Clock { get; } = new();
        public WithdrawalService Service { get; }

        public Setup(decimal matureEarning = 288_000m)
        {
            Clock.Set(Now);
            Service = new WithdrawalService(Repository, new LedgerBalanceService(Ledger, Disputes, Clock), Clock);
            if (matureEarning > 0) Earn(matureEarning, Now.AddHours(-1), orderId: 1);
        }

        public void Earn(decimal amount, DateTime availableAt, long orderId) =>
            Ledger.Entries.Add(new LedgerEntry
            {
                EntryId = Ledger.Entries.Count + 1,
                FromPartyType = LedgerPartyType.PlatformClearing,
                ToPartyType = LedgerPartyType.Worker,
                ToPartyId = Worker,
                Amount = amount,
                EntryType = LedgerEntryType.JobEarning,
                OrderId = orderId,
                AvailableAt = availableAt,
                CreatedAt = Now,
            });
    }

    private static async Task<T> Thrown<T>(Func<Task> action) where T : Exception => await Assert.ThrowsAsync<T>(action);

    [Fact]
    public async Task Wallet_shows_balance_withdrawable_held_pending_request_and_next_maturity()
    {
        var setup = new Setup(188_000m);
        setup.Earn(100_000m, Now.AddHours(10), orderId: 2); // not yet withdrawable
        var empty = await setup.Service.GetWalletAsync(Worker);
        Assert.Equal((288_000m, 188_000m, 0m, null, Now.AddHours(10)), (empty.Balance, empty.Available, empty.Held, empty.PendingWithdrawal, empty.NextAvailableAt));

        var created = await setup.Service.CreateAsync(Worker, 150_000m);
        setup.Ledger.Held[Worker] = 150_000m; // what the SQL query reports for the pending request
        var wallet = await setup.Service.GetWalletAsync(Worker);
        Assert.Equal((288_000m, 38_000m, 150_000m), (wallet.Balance, wallet.Available, wallet.Held));
        Assert.Equal((created.WithdrawalId, 150_000m, "PENDING"), (wallet.PendingWithdrawal!.WithdrawalId, wallet.PendingWithdrawal.Amount, wallet.PendingWithdrawal.Status));
    }

    [Fact]
    public async Task Request_is_pending_copies_the_bank_details_and_runs_under_the_worker_lock()
    {
        var setup = new Setup();
        var created = await setup.Service.CreateAsync(Worker, 100_000m);

        Assert.Equal((100_000m, "PENDING", Now, null, null), (created.Amount, created.Status, created.RequestedAt, created.ReviewedAt, created.RejectReason));
        var row = Assert.Single(setup.Repository.Rows);
        Assert.Equal(("0123456789", "ACB", WithdrawalStatus.Pending), (row.BankAccountNo, row.BankName, row.RequestStatus));
        Assert.Equal(1, setup.Repository.LockedCalls);
    }

    [Fact]
    public async Task The_whole_withdrawable_amount_is_allowed_and_one_dong_more_is_refused()
    {
        var setup = new Setup(188_000m);
        var refused = await Thrown<ValidationException>(() => setup.Service.CreateAsync(Worker, 188_001m));
        Assert.Contains("amount", refused.Errors.Keys);
        Assert.Empty(setup.Repository.Rows);

        Assert.Equal(188_000m, (await setup.Service.CreateAsync(Worker, 188_000m)).Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5000)]
    [InlineData(1000.5)]
    public async Task An_amount_that_is_not_a_positive_whole_number_is_a_field_error(double amount)
    {
        var setup = new Setup();
        var error = await Thrown<ValidationException>(() => setup.Service.CreateAsync(Worker, (decimal)amount));
        Assert.Contains("amount", error.Errors.Keys);
        Assert.Empty(setup.Repository.Rows);
    }

    [Fact]
    public async Task Earnings_younger_than_24_hours_and_orders_with_an_open_dispute_cannot_be_withdrawn()
    {
        var setup = new Setup(0m);
        setup.Earn(100_000m, Now.AddMinutes(1), orderId: 1); // matures in a minute
        setup.Earn(50_000m, Now.AddHours(-1), orderId: 2);
        setup.Disputes.OpenOrderIds.Add(2);

        var wallet = await setup.Service.GetWalletAsync(Worker);
        Assert.Equal((150_000m, 0m), (wallet.Balance, wallet.Available));
        await Thrown<ValidationException>(() => setup.Service.CreateAsync(Worker, 1_000m));

        setup.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(100_000m, (await setup.Service.GetWalletAsync(Worker)).Available);
    }

    [Fact]
    public async Task A_second_request_is_a_409_with_the_pending_code_until_the_first_is_cancelled()
    {
        var setup = new Setup();
        var first = await setup.Service.CreateAsync(Worker, 100_000m);

        var conflict = await Thrown<BusinessRuleViolationException>(() => setup.Service.CreateAsync(Worker, 50_000m));
        Assert.Equal("WITHDRAWAL_PENDING", conflict.Code);

        var cancelled = await setup.Service.CancelAsync(Worker, first.WithdrawalId);
        Assert.Equal("CANCELLED", cancelled.Status);
        Assert.Equal("PENDING", (await setup.Service.CreateAsync(Worker, 50_000m)).Status);
    }

    [Fact]
    public async Task When_the_unique_index_fires_after_the_check_the_answer_is_the_same_409()
    {
        var setup = new Setup();
        setup.Repository.IndexRejectsNextAdd = true; // another request slipped in between the check and the insert
        var conflict = await Thrown<BusinessRuleViolationException>(() => setup.Service.CreateAsync(Worker, 100_000m));
        Assert.Equal("WITHDRAWAL_PENDING", conflict.Code);
    }

    [Fact]
    public async Task Agency_staff_get_403_everywhere_and_an_unknown_worker_404()
    {
        var setup = new Setup();
        setup.Repository.Workers[9] = new WithdrawalWorker(WorkerType.AgencyStaff, null, null);

        await Thrown<ForbiddenAccessException>(() => setup.Service.GetWalletAsync(9));
        await Thrown<ForbiddenAccessException>(() => setup.Service.CreateAsync(9, 1_000m));
        await Thrown<ForbiddenAccessException>(() => setup.Service.ListAsync(9, null, null));
        await Thrown<ForbiddenAccessException>(() => setup.Service.CancelAsync(9, 1));
        await Thrown<NotFoundException>(() => setup.Service.GetWalletAsync(404));
    }

    [Fact]
    public async Task Cancel_of_a_foreign_or_missing_request_is_a_404_and_of_a_decided_one_a_409()
    {
        var setup = new Setup();
        setup.Repository.Workers[8] = new WithdrawalWorker(WorkerType.Freelancer, null, null);
        var mine = await setup.Service.CreateAsync(Worker, 100_000m);

        await Thrown<NotFoundException>(() => setup.Service.CancelAsync(8, mine.WithdrawalId)); // somebody else's
        await Thrown<NotFoundException>(() => setup.Service.CancelAsync(Worker, 999));
        Assert.Equal(WithdrawalStatus.Pending, setup.Repository.Rows.Single().RequestStatus);

        await setup.Service.CancelAsync(Worker, mine.WithdrawalId);
        var decided = await Thrown<BusinessRuleViolationException>(() => setup.Service.CancelAsync(Worker, mine.WithdrawalId));
        Assert.Equal("WITHDRAWAL_NOT_PENDING", decided.Code);

        setup.Repository.Rows.Add(new WithdrawalRequest { WithdrawalId = 50, WorkerId = Worker, Amount = 1_000m, RequestStatus = WithdrawalStatus.Paid, RequestedAt = Now });
        var paid = await Thrown<BusinessRuleViolationException>(() => setup.Service.CancelAsync(Worker, 50));
        Assert.Equal("WITHDRAWAL_NOT_PENDING", paid.Code);
    }

    [Fact]
    public async Task List_returns_only_own_requests_newest_first_with_paging()
    {
        var setup = new Setup();
        setup.Repository.Workers[8] = new WithdrawalWorker(WorkerType.Freelancer, null, null);
        for (var i = 1; i <= 3; i++)
        {
            setup.Repository.Rows.Add(new WithdrawalRequest
            {
                WithdrawalId = i,
                WorkerId = Worker,
                Amount = i * 1_000m,
                RequestStatus = WithdrawalStatus.Paid,
                RequestedAt = Now.AddDays(-4 + i),
            });
        }

        setup.Repository.Rows.Add(new WithdrawalRequest { WithdrawalId = 9, WorkerId = 8, Amount = 777m, RequestStatus = WithdrawalStatus.Paid, RequestedAt = Now });

        var page = await setup.Service.ListAsync(Worker, 1, 2);
        Assert.Equal((1, 2, 3), (page.Page, page.PageSize, page.Total));
        Assert.Equal([3, 2], page.Items.Select(i => i.WithdrawalId).ToArray());
        var second = await setup.Service.ListAsync(Worker, 2, 2);
        Assert.Equal([1], second.Items.Select(i => i.WithdrawalId).ToArray());
        Assert.Equal(20, (await setup.Service.ListAsync(Worker, null, null)).PageSize);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(null, 0)]
    [InlineData(null, 101)]
    public async Task List_rejects_a_bad_page_or_page_size(int? page, int? pageSize)
    {
        var setup = new Setup();
        var error = await Thrown<ValidationException>(() => setup.Service.ListAsync(Worker, page, pageSize));
        Assert.NotEmpty(error.Errors);
    }

    // --- controller ---------------------------------------------------------------------------------------------------

    private class StubUser(int? id) : ICurrentUser
    {
        public bool IsAuthenticated => id is not null;
        public int? UserId => id;
        public UserRole? Role => id is null ? null : UserRole.Worker;
    }

    [Fact]
    public async Task Controller_is_worker_only_uses_the_token_id_and_answers_201_for_a_request()
    {
        var authorize = typeof(WorkerWalletController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.Equal("WorkerOnly", authorize.Policy);
        Assert.Equal("api/v1/workers/me", ((RouteAttribute)typeof(WorkerWalletController).GetCustomAttributes(typeof(RouteAttribute), true).Single()).Template);

        var setup = new Setup();
        var controller = new WorkerWalletController(setup.Service, null!, new StubUser(Worker));
        var created = Assert.IsType<ObjectResult>(await controller.Create(new CreateWithdrawalRequestDto(100_000m), default));
        Assert.Equal(201, created.StatusCode);
        Assert.Equal(Worker, setup.Repository.Rows.Single().WorkerId);
        Assert.IsType<OkObjectResult>(await controller.Wallet(default));
        Assert.IsType<OkObjectResult>(await controller.List(null, null, default));
        Assert.IsType<OkObjectResult>(await controller.Cancel(1, default));
        Assert.IsType<BadRequestObjectResult>(await controller.Create(null, default));
    }

    [Fact]
    public async Task Controller_answers_401_without_a_user_id()
    {
        var controller = new WorkerWalletController(new Setup().Service, null!, new StubUser(null));
        Assert.IsType<UnauthorizedObjectResult>(await controller.Wallet(default));
        Assert.IsType<UnauthorizedObjectResult>(await controller.Create(new CreateWithdrawalRequestDto(1m), default));
        Assert.IsType<UnauthorizedObjectResult>(await controller.List(null, null, default));
        Assert.IsType<UnauthorizedObjectResult>(await controller.Cancel(1, default));
    }
}
