using CommonService.Application.Exceptions;
using CommonService.Application.Features.Ledger;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.WebAPI.Controllers.Ledger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Tests.Ledger;

/// <summary>The Admin's decision on advance requests (ticket LEDGER-08, decision E).</summary>
public class AdminWithdrawalTests
{
    private const int Worker = 7;
    private const int Admin = 1;
    private static readonly DateTime Now = new(2026, 11, 10, 4, 0, 0, DateTimeKind.Utc);

    /// <summary>The balance service reads the same entries the writer appends, and the pending request is the held amount.</summary>
    private class MemoryLedgerQuery(MemoryLedger ledger, MemoryWithdrawals withdrawals) : ILedgerQuery
    {
        public Task<IReadOnlyList<LedgerEntry>> GetPartyEntriesAsync(LedgerPartyType type, int? id, DateTime? beforeUtc = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LedgerEntry>>(ledger.Rows.Where(e =>
                ((e.ToPartyType == type && e.ToPartyId == id) || (e.FromPartyType == type && e.FromPartyId == id)) &&
                (!beforeUtc.HasValue || e.CreatedAt < beforeUtc.Value)).ToArray());

        public Task<decimal> GetHeldAsync(int workerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(withdrawals.Rows.Where(r => r.WorkerId == workerId && r.RequestStatus == WithdrawalStatus.Pending).Sum(r => r.Amount));

        public Task<DateTime?> GetFirstEntryAtAsync(CancellationToken cancellationToken = default) => Task.FromResult<DateTime?>(null);
    }

    /// <summary>Records whether the worker lock was held while the ledger entry was appended.</summary>
    private class ProbeWriter(ILedgerWriter inner, MemoryWithdrawals withdrawals) : ILedgerWriter
    {
        public List<bool> LockedAtAppend { get; } = [];

        public Task<IReadOnlyList<LedgerEntry>> AppendAsync(IReadOnlyList<NewLedgerEntry> entries, CancellationToken cancellationToken = default)
        {
            LockedAtAppend.Add(withdrawals.HoldsLock);
            return inner.AppendAsync(entries, cancellationToken);
        }

        public Task<IReadOnlyList<LedgerEntry>> AppendAsync(Func<ILedgerLockedReader, Task<IReadOnlyList<NewLedgerEntry>>> build, CancellationToken cancellationToken = default) =>
            inner.AppendAsync(build, cancellationToken);
    }

    private class ThrowingNotifications : INotificationService
    {
        public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default) => throw new InvalidOperationException("push is down");
    }

    private class Setup
    {
        public MemoryLedger Ledger { get; } = new();
        public MemoryWithdrawals Repository { get; } = new();
        public FakeAuditLog Audit { get; } = new();
        public FakeNotificationService Notifications { get; } = new();
        public FakeClock Clock { get; } = new();
        public ProbeWriter Writer { get; }
        public WithdrawalService Wallet { get; }
        public AdminWithdrawalService Admin { get; }

        public Setup(decimal earning = 288_000m, INotificationService? notifications = null)
        {
            Clock.Set(Now);
            Writer = new ProbeWriter(new LedgerWriter(Ledger, Clock), Repository);
            var balances = new LedgerBalanceService(new MemoryLedgerQuery(Ledger, Repository), new FakeOpenDisputeQuery(), Clock);
            Wallet = new WithdrawalService(Repository, balances, Clock);
            Admin = new AdminWithdrawalService(Repository, balances, Writer, Audit, notifications ?? Notifications, Clock, NullLogger<AdminWithdrawalService>.Instance);
            if (earning > 0) Add(LedgerRules.Clearing, new LedgerParty(LedgerPartyType.Worker, Worker), earning, LedgerEntryType.JobEarning, "earn", Now.AddHours(-1));
        }

        public void Add(LedgerParty from, LedgerParty to, decimal amount, LedgerEntryType type, string key, DateTime? availableAt = null) =>
            new LedgerWriter(Ledger, Clock).AppendAsync([new NewLedgerEntry(from, to, amount, type, LedgerRefType.Assignment, 1, null, availableAt, key)]).Wait();

        public void Penalise(decimal amount) =>
            Add(new LedgerParty(LedgerPartyType.Worker, Worker), LedgerRules.Revenue, amount, LedgerEntryType.Penalty, "penalty:" + Guid.NewGuid());

        public IReadOnlyList<LedgerEntry> Withdrawals => Ledger.Rows.Where(e => e.EntryType == LedgerEntryType.Withdrawal).ToArray();
        public decimal WorkerBalance => Ledger.Rows.Where(e => e.ToPartyType == LedgerPartyType.Worker).Sum(e => e.Amount)
            - Ledger.Rows.Where(e => e.FromPartyType == LedgerPartyType.Worker).Sum(e => e.Amount);
    }

    private static async Task<T> Thrown<T>(Func<Task> action) where T : Exception => await Assert.ThrowsAsync<T>(action);

    [Fact]
    public async Task Approval_writes_one_withdrawal_entry_pays_the_request_audits_and_tells_the_worker()
    {
        var setup = new Setup();
        var request = await setup.Wallet.CreateAsync(Worker, 100_000m);

        var approved = await setup.Admin.ApproveAsync(Admin, request.WithdrawalId);

        Assert.Equal(("PAID", 100_000m), (approved.Status, approved.Amount));
        Assert.NotNull(approved.ReviewedAt);
        var entry = Assert.Single(setup.Withdrawals);
        Assert.Equal((LedgerPartyType.Worker, Worker, LedgerPartyType.External, 100_000m, "WITHDRAWAL:1", LedgerRefType.Withdrawal, 1L, Admin),
            (entry.FromPartyType, entry.FromPartyId!.Value, entry.ToPartyType, entry.Amount, entry.IdempotencyKey, entry.RefType, entry.RefId, entry.CreatedByAdminId!.Value));
        Assert.Null(entry.ToPartyId);

        var row = setup.Repository.Rows.Single();
        Assert.Equal((WithdrawalStatus.Paid, entry.EntryId, Admin, Now), (row.RequestStatus, row.EntryId, row.ReviewedBy, row.ReviewedAt));
        Assert.Equal(188_000m, setup.WorkerBalance);

        var audit = Assert.Single(setup.Audit.Entries);
        Assert.Equal((AuditActorType.Admin, Admin, "WITHDRAWAL_REQUEST", "1", "request_status", "PENDING", "PAID"),
            (audit.ActorType, audit.AdminId!.Value, audit.EntityType, audit.EntityId, audit.FieldName, audit.OldValue, audit.NewValue));
        Assert.False(string.IsNullOrWhiteSpace(audit.Reason));
        Assert.True(setup.Repository.Saves >= 1);

        var message = Assert.Single(setup.Notifications.Sent);
        Assert.Equal((UserRole.Worker, Worker, "withdrawal.approved"), (message.RecipientRole, message.RecipientId, message.Topic));
        Assert.Contains("100", message.Body);
        Assert.Equal("1", message.Data!["withdrawalId"]);
    }

    [Fact]
    public async Task The_entry_is_appended_while_the_worker_lock_is_held()
    {
        var setup = new Setup();
        var request = await setup.Wallet.CreateAsync(Worker, 100_000m);
        await setup.Admin.ApproveAsync(Admin, request.WithdrawalId);
        Assert.Equal([true], setup.Writer.LockedAtAppend);
    }

    [Fact]
    public async Task Two_approvals_at_once_write_one_entry_and_the_other_gets_not_pending()
    {
        var setup = new Setup();
        var request = await setup.Wallet.CreateAsync(Worker, 100_000m);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { return (await setup.Admin.ApproveAsync(Admin, request.WithdrawalId), (string?)null); }
            catch (BusinessRuleViolationException ex) { return (null!, ex.Code); }
        }));

        Assert.Single(outcomes, o => o.Item2 is null);
        Assert.Single(outcomes, o => o.Item2 == "WITHDRAWAL_NOT_PENDING");
        Assert.Single(setup.Withdrawals);
        Assert.Single(setup.Audit.Entries);
        Assert.Single(setup.Notifications.Sent);
        Assert.Equal(188_000m, setup.WorkerBalance);
    }

    [Fact]
    public async Task A_decided_or_cancelled_or_unknown_request_cannot_be_approved()
    {
        var setup = new Setup();
        var paid = await setup.Wallet.CreateAsync(Worker, 100_000m);
        await setup.Admin.ApproveAsync(Admin, paid.WithdrawalId);
        Assert.Equal("WITHDRAWAL_NOT_PENDING", (await Thrown<BusinessRuleViolationException>(() => setup.Admin.ApproveAsync(Admin, paid.WithdrawalId))).Code);

        var cancelled = await setup.Wallet.CreateAsync(Worker, 10_000m);
        await setup.Wallet.CancelAsync(Worker, cancelled.WithdrawalId);
        Assert.Equal("WITHDRAWAL_NOT_PENDING", (await Thrown<BusinessRuleViolationException>(() => setup.Admin.ApproveAsync(Admin, cancelled.WithdrawalId))).Code);
        Assert.Single(setup.Withdrawals);

        await Thrown<NotFoundException>(() => setup.Admin.ApproveAsync(Admin, 999));
        await Thrown<NotFoundException>(() => setup.Admin.RejectAsync(Admin, 999, "x"));
    }

    [Fact]
    public async Task A_penalty_after_the_request_can_make_it_unpayable_and_the_admin_can_only_reject()
    {
        var setup = new Setup();
        var request = await setup.Wallet.CreateAsync(Worker, 100_000m);
        setup.Penalise(250_000m); // withdrawable before the hold: 288,000 - 250,000 = 38,000

        var conflict = await Thrown<BusinessRuleViolationException>(() => setup.Admin.ApproveAsync(Admin, request.WithdrawalId));
        Assert.Equal("WITHDRAWAL_INSUFFICIENT", conflict.Code);
        Assert.Empty(setup.Withdrawals);
        Assert.Equal(WithdrawalStatus.Pending, setup.Repository.Rows.Single().RequestStatus);
        Assert.Empty(setup.Audit.Entries);
        Assert.Empty(setup.Notifications.Sent);

        var rejected = await setup.Admin.RejectAsync(Admin, request.WithdrawalId, "Còn khoản khấu trừ mới");
        Assert.Equal("REJECTED", rejected.Status);
    }

    [Fact]
    public async Task A_smaller_penalty_leaves_enough_because_the_requests_own_hold_counts_again()
    {
        var setup = new Setup();
        var request = await setup.Wallet.CreateAsync(Worker, 100_000m);
        setup.Penalise(50_000m); // 288,000 - 50,000 - 100,000 held would show 138,000, but the request itself is what is approved

        var approved = await setup.Admin.ApproveAsync(Admin, request.WithdrawalId);
        Assert.Equal("PAID", approved.Status);
        Assert.Equal(288_000m - 50_000m - 100_000m, setup.WorkerBalance);
    }

    [Fact]
    public async Task Approval_that_would_overdraw_after_the_balance_was_spent_elsewhere_is_refused()
    {
        var setup = new Setup(100_000m);
        var request = await setup.Wallet.CreateAsync(Worker, 100_000m);
        setup.Add(new LedgerParty(LedgerPartyType.Worker, Worker), new LedgerParty(LedgerPartyType.External, null), 1m, LedgerEntryType.Withdrawal, "other");

        Assert.Equal("WITHDRAWAL_INSUFFICIENT", (await Thrown<BusinessRuleViolationException>(() => setup.Admin.ApproveAsync(Admin, request.WithdrawalId))).Code);
    }

    [Fact]
    public async Task Rejection_writes_no_entry_keeps_the_reason_audits_and_frees_the_worker_to_ask_again()
    {
        var setup = new Setup();
        var request = await setup.Wallet.CreateAsync(Worker, 100_000m);

        var rejected = await setup.Admin.RejectAsync(Admin, request.WithdrawalId, "  Thiếu chứng từ  ");

        Assert.Equal(("REJECTED", "Thiếu chứng từ"), (rejected.Status, rejected.RejectReason));
        Assert.Empty(setup.Withdrawals);
        Assert.Equal(288_000m, setup.WorkerBalance);
        var row = setup.Repository.Rows.Single();
        Assert.Equal((WithdrawalStatus.Rejected, (long?)null, Admin), (row.RequestStatus, row.EntryId, row.ReviewedBy));

        var audit = Assert.Single(setup.Audit.Entries);
        Assert.Equal(("PENDING", "REJECTED", "Thiếu chứng từ"), (audit.OldValue, audit.NewValue, audit.Reason));
        var message = Assert.Single(setup.Notifications.Sent);
        Assert.Equal("withdrawal.rejected", message.Topic);
        Assert.Contains("Thiếu chứng từ", message.Body);

        Assert.Equal("PENDING", (await setup.Wallet.CreateAsync(Worker, 50_000m)).Status);
        Assert.Equal("WITHDRAWAL_NOT_PENDING", (await Thrown<BusinessRuleViolationException>(() => setup.Admin.RejectAsync(Admin, request.WithdrawalId, "again"))).Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_rejection_needs_a_reason(string? reason)
    {
        var setup = new Setup();
        var request = await setup.Wallet.CreateAsync(Worker, 100_000m);
        Assert.Contains("reason", (await Thrown<ValidationException>(() => setup.Admin.RejectAsync(Admin, request.WithdrawalId, reason))).Errors.Keys);
        Assert.Equal(WithdrawalStatus.Pending, setup.Repository.Rows.Single().RequestStatus);
    }

    [Fact]
    public async Task A_reason_of_255_characters_is_accepted_and_256_is_not()
    {
        var setup = new Setup();
        var request = await setup.Wallet.CreateAsync(Worker, 100_000m);
        await Thrown<ValidationException>(() => setup.Admin.RejectAsync(Admin, request.WithdrawalId, new string('x', 256)));
        Assert.Equal(255, (await setup.Admin.RejectAsync(Admin, request.WithdrawalId, new string('x', 255))).RejectReason!.Length);
    }

    [Fact]
    public async Task A_failing_push_does_not_undo_the_decision()
    {
        var setup = new Setup(notifications: new ThrowingNotifications());
        var request = await setup.Wallet.CreateAsync(Worker, 100_000m);
        Assert.Equal("PAID", (await setup.Admin.ApproveAsync(Admin, request.WithdrawalId)).Status);
        Assert.Single(setup.Withdrawals);
    }

    [Fact]
    public async Task List_filters_by_status_oldest_pending_first_and_names_the_worker()
    {
        var setup = new Setup();
        setup.Repository.Workers[8] = new WithdrawalWorker(WorkerType.Freelancer, null, null);
        setup.Repository.Rows.Add(new WithdrawalRequest { WithdrawalId = 11, WorkerId = Worker, Amount = 1_000m, RequestStatus = WithdrawalStatus.Pending, RequestedAt = Now.AddHours(-1) });
        setup.Repository.Rows.Add(new WithdrawalRequest { WithdrawalId = 12, WorkerId = 8, Amount = 2_000m, RequestStatus = WithdrawalStatus.Pending, RequestedAt = Now.AddHours(-3) });
        setup.Repository.Rows.Add(new WithdrawalRequest { WithdrawalId = 13, WorkerId = 8, Amount = 3_000m, RequestStatus = WithdrawalStatus.Paid, RequestedAt = Now.AddHours(-5), EntryId = 4 });
        setup.Repository.Rows.Add(new WithdrawalRequest { WithdrawalId = 14, WorkerId = 8, Amount = 4_000m, RequestStatus = WithdrawalStatus.Rejected, RequestedAt = Now.AddHours(-6), RejectReason = "x" });

        var pending = await setup.Admin.ListAsync("pending", null, null);
        Assert.Equal([12, 11], pending.Items.Select(i => i.WithdrawalId).ToArray());
        Assert.Equal((2, 1, 20), (pending.Total, pending.Page, pending.PageSize));
        Assert.Equal(("Worker 8", 8), (pending.Items[0].WorkerName, pending.Items[0].WorkerId));

        var all = await setup.Admin.ListAsync(null, 1, 3);
        Assert.Equal([11, 12, 13], all.Items.Select(i => i.WithdrawalId).ToArray());
        Assert.Equal(4, all.Total);
        Assert.Equal(4L, (await setup.Admin.ListAsync("PAID", null, null)).Items.Single().EntryId);
        Assert.Equal("x", (await setup.Admin.ListAsync("REJECTED", null, null)).Items.Single().RejectReason);
    }

    [Theory]
    [InlineData("DONE", null, null)]
    [InlineData(null, 0, null)]
    [InlineData(null, null, 101)]
    public async Task List_rejects_a_bad_status_or_paging(string? status, int? page, int? pageSize)
    {
        var setup = new Setup();
        Assert.NotEmpty((await Thrown<ValidationException>(() => setup.Admin.ListAsync(status, page, pageSize))).Errors);
    }

    // --- controller ---------------------------------------------------------------------------------------------------

    private class StubUser(int? id) : ICurrentUser
    {
        public bool IsAuthenticated => id is not null;
        public int? UserId => id;
        public UserRole? Role => id is null ? null : UserRole.Admin;
    }

    [Fact]
    public async Task Controller_is_admin_only_takes_the_admin_id_from_the_token_and_answers_401_without_it()
    {
        var authorize = typeof(AdminWithdrawalsController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.Equal("AdminOnly", authorize.Policy);
        Assert.Equal("api/v1/admin/withdrawals", ((RouteAttribute)typeof(AdminWithdrawalsController).GetCustomAttributes(typeof(RouteAttribute), true).Single()).Template);

        var setup = new Setup();
        var controller = new AdminWithdrawalsController(setup.Admin, new StubUser(Admin));
        var first = await setup.Wallet.CreateAsync(Worker, 100_000m);
        Assert.IsType<OkObjectResult>(await controller.List("PENDING", null, null, default));
        Assert.IsType<OkObjectResult>(await controller.Reject(first.WithdrawalId, new RejectWithdrawalRequestDto("no"), default));
        Assert.Equal(Admin, setup.Repository.Rows.Single().ReviewedBy);
        var second = await setup.Wallet.CreateAsync(Worker, 100_000m);
        Assert.IsType<OkObjectResult>(await controller.Approve(second.WithdrawalId, default));
        await Thrown<ValidationException>(() => controller.Reject(second.WithdrawalId, null, default));

        var anonymous = new AdminWithdrawalsController(setup.Admin, new StubUser(null));
        Assert.IsType<UnauthorizedObjectResult>(await anonymous.Approve(1, default));
        Assert.IsType<UnauthorizedObjectResult>(await anonymous.Reject(1, new RejectWithdrawalRequestDto("x"), default));
    }
}
