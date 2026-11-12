using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Features.Workers.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MsOptions = Microsoft.Extensions.Options.Options;
using Xunit;

namespace CommonService.Tests.Workers;

/// <summary>LEDGER-28: the payout account is set once, and changed by a request an Admin approves, with a 15-day lock from the day it is sent.</summary>
public class WorkerBankChangeTests
{
    private const int NoAccount = 7;
    private const int HasAccount = 8;
    private const int Admin = 1;
    private static readonly DateTime Start = new(2026, 11, 10, 4, 0, 0, DateTimeKind.Utc);

    private class User(int? id) : ICurrentUser
    {
        public int? UserId { get; } = id;
        public UserRole? Role { get; } = UserRole.Worker;
        public bool IsAuthenticated => id.HasValue;
    }

    private class Rig
    {
        public MemoryBankChanges Repository { get; } = new();
        public FakeClock Clock { get; } = new();
        public FakeAuditLog Audit { get; } = new();
        public FakeNotificationService Notifications { get; } = new();
        public IAdminBankChangeService Admin { get; }
        private readonly IOptions<BusinessRules> _rules = MsOptions.Create(new BusinessRules());

        public Rig()
        {
            Clock.Set(Start);
            Admin = new AdminBankChangeService(Repository, Audit, Notifications, Clock, NullLogger<AdminBankChangeService>.Instance);
        }

        public IWorkerBankService For(int workerId) => new WorkerBankService(Repository, new User(workerId), Clock, _rules);
    }

    private static async Task<BusinessRuleViolationException> Conflict(Task task) => await Assert.ThrowsAsync<BusinessRuleViolationException>(() => task);

    // ---- the first account -------------------------------------------------------------------------------------

    [Fact]
    public async Task The_first_account_is_saved_at_once_with_no_request_and_no_lock()
    {
        var rig = new Rig();

        var saved = await rig.For(NoAccount).SetFirstAsync(new UpdateWorkerBankRequest(" Vietcombank ", "0011 0012 3456"), default);

        Assert.Equal(("Vietcombank", "001100123456", true), (saved.BankName, saved.BankAccountNo, saved.IsSet));
        Assert.Empty(rig.Repository.Rows);
        Assert.True(saved.CanRequestChange);
        Assert.Null(saved.ChangeLockedUntil);
    }

    [Fact]
    public async Task Setting_again_on_a_worker_who_has_an_account_says_to_send_a_request()
    {
        var rig = new Rig();

        var error = await Conflict(rig.For(HasAccount).SetFirstAsync(new UpdateWorkerBankRequest("VCB", "9876543210"), default));

        Assert.Equal(BankChangeErrorCodes.RequiresRequest, error.Code);
        Assert.Equal("0123456789", rig.Repository.Workers[HasAccount].BankAccountNo);
    }

    [Fact]
    public async Task A_missing_body_a_bad_field_and_an_anonymous_caller_are_refused()
    {
        var rig = new Rig();
        await Assert.ThrowsAsync<ValidationException>(() => rig.For(NoAccount).SetFirstAsync(null, default));
        await Assert.ThrowsAsync<ValidationException>(() => rig.For(NoAccount).SetFirstAsync(new UpdateWorkerBankRequest("VCB", "12"), default));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            new WorkerBankService(rig.Repository, new User(null), rig.Clock, MsOptions.Create(new BusinessRules())).GetAsync(default));
    }

    // ---- sending a request -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_worker_without_an_account_cannot_send_a_change_request()
    {
        var rig = new Rig();

        var error = await Conflict(rig.For(NoAccount).RequestChangeAsync(new CreateBankChangeRequest("VCB", "9876543210"), default));

        Assert.Equal(BankChangeErrorCodes.NotSet, error.Code);
    }

    [Fact]
    public async Task A_request_is_pending_keeps_the_old_account_and_does_not_change_it()
    {
        var rig = new Rig();

        var state = await rig.For(HasAccount).RequestChangeAsync(new CreateBankChangeRequest("Vietcombank", "9876 5432 10"), default);

        var row = Assert.Single(rig.Repository.Rows);
        Assert.Equal((BankChangeStatus.Pending, "Vietcombank", "9876543210", "ACB", "0123456789", Start), (row.RequestStatus, row.BankName, row.BankAccountNo, row.PreviousBankName, row.PreviousBankAccountNo, row.RequestedAt));
        Assert.Equal("0123456789", rig.Repository.Workers[HasAccount].BankAccountNo);
        Assert.False(state.CanRequestChange);
        Assert.Equal("9876543210", state.PendingRequest!.BankAccountNo);
        Assert.Equal(Start.AddDays(15), state.ChangeLockedUntil);
    }

    [Fact]
    public async Task The_same_account_is_refused()
    {
        var rig = new Rig();

        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            rig.For(HasAccount).RequestChangeAsync(new CreateBankChangeRequest("acb", "0123 456 789"), default));

        Assert.Contains("bankAccountNo", error.Errors.Keys);
        Assert.Empty(rig.Repository.Rows);
    }

    [Fact]
    public async Task A_second_request_while_one_is_pending_is_refused()
    {
        var rig = new Rig();
        await rig.For(HasAccount).RequestChangeAsync(new CreateBankChangeRequest("VCB", "1111111111"), default);

        var error = await Conflict(rig.For(HasAccount).RequestChangeAsync(new CreateBankChangeRequest("VCB", "2222222222"), default));

        Assert.Equal(BankChangeErrorCodes.Pending, error.Code);
        Assert.Single(rig.Repository.Rows);
    }

    [Fact]
    public async Task The_unique_index_is_the_last_word_when_two_requests_race()
    {
        var rig = new Rig();
        rig.Repository.IndexRejectsNextAdd = true;

        var error = await Conflict(rig.For(HasAccount).RequestChangeAsync(new CreateBankChangeRequest("VCB", "1111111111"), default));

        Assert.Equal(BankChangeErrorCodes.Pending, error.Code);
    }

    // ---- the 15-day lock ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task After_a_decision_a_new_request_is_locked_for_15_days_from_the_day_it_was_sent_approved_or_rejected(bool approve)
    {
        var rig = new Rig();
        await rig.For(HasAccount).RequestChangeAsync(new CreateBankChangeRequest("VCB", "1111111111"), default);
        rig.Clock.Set(Start.AddDays(3)); // decided on day 3: the lock still runs from the day it was SENT
        if (approve) await rig.Admin.ApproveAsync(Admin, 1, default);
        else await rig.Admin.RejectAsync(Admin, 1, "Số tài khoản không khớp", default);

        rig.Clock.Set(Start.AddDays(14).AddHours(23));
        var error = await Conflict(rig.For(HasAccount).RequestChangeAsync(new CreateBankChangeRequest("VCB", "2222222222"), default));

        Assert.Equal(BankChangeErrorCodes.Locked, error.Code);
        Assert.Contains("25/11/2026", error.Message); // 10/11 + 15 days, shown as a Vietnam date
        Assert.False((await rig.For(HasAccount).GetAsync(default)).CanRequestChange);
    }

    [Fact]
    public async Task On_the_15th_day_a_new_request_is_allowed()
    {
        var rig = new Rig();
        await rig.For(HasAccount).RequestChangeAsync(new CreateBankChangeRequest("VCB", "1111111111"), default);
        await rig.Admin.RejectAsync(Admin, 1, "Chưa rõ", default);

        rig.Clock.Set(Start.AddDays(15));
        var state = await rig.For(HasAccount).GetAsync(default);
        Assert.True(state.CanRequestChange);
        Assert.Null(state.ChangeLockedUntil);

        await rig.For(HasAccount).RequestChangeAsync(new CreateBankChangeRequest("VCB", "2222222222"), default);
        Assert.Equal(2, rig.Repository.Rows.Count);
    }

    [Fact]
    public async Task The_lock_length_comes_from_the_business_rules()
    {
        var rig = new Rig();
        var service = new WorkerBankService(rig.Repository, new User(HasAccount), rig.Clock, MsOptions.Create(new BusinessRules { Worker = new WorkerRules { BankChangeLockDays = 3 } }));
        await service.RequestChangeAsync(new CreateBankChangeRequest("VCB", "1111111111"), default);
        await rig.Admin.RejectAsync(Admin, 1, "Chưa rõ", default);

        rig.Clock.Set(Start.AddDays(3));

        Assert.True((await service.GetAsync(default)).CanRequestChange);
    }

    [Fact]
    public void The_default_lock_is_15_days() => Assert.Equal(15, new BusinessRules().Worker.BankChangeLockDays);

    // ---- what the worker reads ---------------------------------------------------------------------------------

    [Fact]
    public async Task The_state_names_the_pending_and_the_latest_request()
    {
        var rig = new Rig();
        var empty = await rig.For(HasAccount).GetAsync(default);
        Assert.True(empty.CanRequestChange);
        Assert.Null(empty.PendingRequest);
        Assert.Null(empty.LastRequest);

        await rig.For(HasAccount).RequestChangeAsync(new CreateBankChangeRequest("VCB", "1111111111"), default);
        await rig.Admin.RejectAsync(Admin, 1, "Sai tên chủ tài khoản", default);

        var state = await rig.For(HasAccount).GetAsync(default);
        Assert.Null(state.PendingRequest);
        Assert.Equal(("REJECTED", "Sai tên chủ tài khoản"), (state.LastRequest!.Status, state.LastRequest.RejectReason));
        Assert.Equal("0123456789", state.BankAccountNo);
    }

    // ---- the Admin ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Approving_writes_the_new_account_audits_and_tells_the_worker()
    {
        var rig = new Rig();
        await rig.For(HasAccount).RequestChangeAsync(new CreateBankChangeRequest("Vietcombank", "9876543210"), default);

        var done = await rig.Admin.ApproveAsync(Admin, 1, default);

        Assert.Equal("APPROVED", done.Status);
        Assert.Equal(("Vietcombank", "9876543210"), (rig.Repository.Workers[HasAccount].BankName, rig.Repository.Workers[HasAccount].BankAccountNo));
        var audit = Assert.Single(rig.Audit.Entries);
        Assert.Equal(("WORKER_BANK_CHANGE", "1", Admin), (audit.EntityType, audit.EntityId, audit.AdminId));
        Assert.DoesNotContain("0123456789", $"{audit.OldValue}{audit.NewValue}"); // only the last 4 digits
        Assert.Contains("6789", audit.OldValue);
        var message = Assert.Single(rig.Notifications.Sent);
        Assert.Equal((UserRole.Worker, HasAccount, BankChangeNotificationTopics.Approved), (message.RecipientRole, message.RecipientId, message.Topic));
        Assert.DoesNotContain("9876543210", message.Body);
    }

    [Fact]
    public async Task Rejecting_keeps_the_account_stores_the_reason_and_tells_the_worker()
    {
        var rig = new Rig();
        await rig.For(HasAccount).RequestChangeAsync(new CreateBankChangeRequest("Vietcombank", "9876543210"), default);

        var done = await rig.Admin.RejectAsync(Admin, 1, "  Sai tên chủ tài khoản  ", default);

        Assert.Equal(("REJECTED", "Sai tên chủ tài khoản"), (done.Status, done.RejectReason));
        Assert.Equal("0123456789", rig.Repository.Workers[HasAccount].BankAccountNo);
        Assert.Equal(BankChangeNotificationTopics.Rejected, Assert.Single(rig.Notifications.Sent).Topic);
        Assert.Single(rig.Audit.Entries);
    }

    [Fact]
    public async Task A_decided_request_cannot_be_decided_again()
    {
        var rig = new Rig();
        await rig.For(HasAccount).RequestChangeAsync(new CreateBankChangeRequest("VCB", "1111111111"), default);
        await rig.Admin.ApproveAsync(Admin, 1, default);

        Assert.Equal(BankChangeErrorCodes.NotPending, (await Conflict(rig.Admin.ApproveAsync(Admin, 1, default))).Code);
        Assert.Equal(BankChangeErrorCodes.NotPending, (await Conflict(rig.Admin.RejectAsync(Admin, 1, "x", default))).Code);
        Assert.Single(rig.Audit.Entries);
    }

    [Fact]
    public async Task A_rejection_needs_a_reason_of_1_to_255_characters_and_an_unknown_request_is_not_found()
    {
        var rig = new Rig();
        await rig.For(HasAccount).RequestChangeAsync(new CreateBankChangeRequest("VCB", "1111111111"), default);

        await Assert.ThrowsAsync<ValidationException>(() => rig.Admin.RejectAsync(Admin, 1, "   ", default));
        await Assert.ThrowsAsync<ValidationException>(() => rig.Admin.RejectAsync(Admin, 1, new string('x', 256), default));
        await Assert.ThrowsAsync<NotFoundException>(() => rig.Admin.ApproveAsync(Admin, 99, default));
        Assert.Empty(rig.Audit.Entries);
    }

    [Fact]
    public async Task A_failing_push_does_not_undo_the_decision()
    {
        var rig = new Rig();
        await rig.For(HasAccount).RequestChangeAsync(new CreateBankChangeRequest("VCB", "1111111111"), default);
        var admin = new AdminBankChangeService(rig.Repository, rig.Audit, new ThrowingNotifications(), rig.Clock, NullLogger<AdminBankChangeService>.Instance);

        var done = await admin.ApproveAsync(Admin, 1, default);

        Assert.Equal("APPROVED", done.Status);
        Assert.Equal("1111111111", rig.Repository.Workers[HasAccount].BankAccountNo);
    }

    [Fact]
    public async Task The_queue_lists_pending_oldest_first_and_filters_by_status()
    {
        var rig = new Rig();
        rig.Repository.Workers[9] = new BankWorker(WorkerType.Freelancer, "ACB", "5555555555");
        await rig.For(HasAccount).RequestChangeAsync(new CreateBankChangeRequest("VCB", "1111111111"), default);
        rig.Clock.Set(Start.AddHours(1));
        await rig.For(9).RequestChangeAsync(new CreateBankChangeRequest("VCB", "2222222222"), default);
        await rig.Admin.RejectAsync(Admin, 1, "Chưa rõ", default);

        var pending = await rig.Admin.ListAsync("PENDING", null, null, default);
        var rejected = await rig.Admin.ListAsync("rejected", 1, 5, default);
        var all = await rig.Admin.ListAsync(null, null, null, default);

        Assert.Equal([2], pending.Items.Select(i => i.RequestId));
        Assert.Equal([1], rejected.Items.Select(i => i.RequestId));
        Assert.Equal(2, all.Total);
        Assert.Equal(("ACB", "5555555555", "VCB", "2222222222"), (pending.Items[0].PreviousBankName, pending.Items[0].PreviousBankAccountNo, pending.Items[0].BankName, pending.Items[0].BankAccountNo));
        await Assert.ThrowsAsync<ValidationException>(() => rig.Admin.ListAsync("PAID", null, null, default));
        await Assert.ThrowsAsync<ValidationException>(() => rig.Admin.ListAsync(null, 0, 500, default));
    }

    [Fact]
    public async Task The_work_runs_under_the_worker_lock()
    {
        var rig = new Rig();
        await rig.For(HasAccount).RequestChangeAsync(new CreateBankChangeRequest("VCB", "1111111111"), default);
        await rig.Admin.ApproveAsync(Admin, 1, default);

        Assert.False(rig.Repository.HoldsLock); // released; the repository asserts the lock inside every write
    }

    private class ThrowingNotifications : INotificationService
    {
        public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default) => throw new InvalidOperationException("push is down");
    }
}
