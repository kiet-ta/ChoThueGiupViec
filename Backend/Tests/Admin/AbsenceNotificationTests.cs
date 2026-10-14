using CommonService.Application.Features.Admin;
using CommonService.Application.Features.Admin.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Tests.Admin;

/// <summary>BE-M6-03b: who is told when a customer absence is reported and when it is approved.</summary>
public class AbsenceNotificationTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 4, 0, 0, DateTimeKind.Utc);

    private sealed class Reads : IAbsenceRepository
    {
        public List<int> Admins { get; } = [];
        public Dictionary<long, int> Customers { get; } = [];
        public bool Throw { get; set; }

        public Task<IReadOnlyList<int>> GetActiveAdminIdsAsync(CancellationToken cancellationToken = default) =>
            Throw ? throw new InvalidOperationException("db down") : Task.FromResult<IReadOnlyList<int>>(Admins.ToList());

        public Task<int?> GetCustomerIdOfOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
            Throw ? throw new InvalidOperationException("db down") : Task.FromResult(Customers.TryGetValue(orderId, out var id) ? id : (int?)null);

        public Task<(IReadOnlyList<AbsenceReportRow> Items, int Total)> SearchAsync(string status, int page, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AbsenceReportRow?> GetAsync(long assignmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> TryMarkAbsentAsync(long assignmentId, decimal absenceFeeAmount, DateTime nowUtc, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FailingNotifier : INotificationService
    {
        public int Calls { get; private set; }

        public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("hub down");
        }
    }

    private static CustomerAbsentReported Reported(long orderId = 900) => new(70, orderId, 41, Now);

    private static CustomerAbsentApproved Approved(long orderId = 900) => new(70, orderId, 41, 104000m, 156000m, Now);

    [Fact]
    public async Task Every_active_admin_is_told_once_about_a_new_report()
    {
        var reads = new Reads { Admins = { 3, 8 } };
        var notifier = new FakeNotificationService();

        await new CustomerAbsentReportedNotificationHandler(reads, notifier, NullLogger<CustomerAbsentReportedNotificationHandler>.Instance)
            .Handle(Reported(), default);

        Assert.Equal([3, 8], notifier.Sent.Select(m => m.RecipientId).ToArray());
        Assert.All(notifier.Sent, m =>
        {
            Assert.Equal(UserRole.Admin, m.RecipientRole);
            Assert.Equal("absence.reported", m.Topic);
            Assert.Contains("#900", m.Body);
            Assert.Equal("70", m.Data!["assignmentId"]);
            Assert.Equal("900", m.Data["orderId"]);
        });
    }

    [Fact]
    public async Task With_no_active_admin_nothing_is_sent_and_nothing_throws()
    {
        var notifier = new FakeNotificationService();

        await new CustomerAbsentReportedNotificationHandler(new Reads(), notifier, NullLogger<CustomerAbsentReportedNotificationHandler>.Instance)
            .Handle(Reported(), default);

        Assert.Empty(notifier.Sent);
    }

    [Fact]
    public async Task The_worker_and_the_customer_learn_the_fee_and_the_refund_in_whole_vnd()
    {
        var reads = new Reads { Customers = { [900] = 11 } };
        var notifier = new FakeNotificationService();

        await new CustomerAbsentApprovedNotificationHandler(reads, notifier, NullLogger<CustomerAbsentApprovedNotificationHandler>.Instance)
            .Handle(Approved(), default);

        var worker = Assert.Single(notifier.Sent, m => m.RecipientRole == UserRole.Worker);
        var customer = Assert.Single(notifier.Sent, m => m.RecipientRole == UserRole.Customer);
        Assert.Equal(41, worker.RecipientId);
        Assert.Contains("104000 đ", worker.Body);
        Assert.Equal(11, customer.RecipientId);
        Assert.Contains("104000 đ", customer.Body);
        Assert.Contains("156000 đ", customer.Body);
        Assert.All(notifier.Sent, m =>
        {
            Assert.Equal("absence.approved", m.Topic);
            Assert.Equal("104000", m.Data!["compensationAmount"]);
            Assert.Equal("156000", m.Data["refundAmount"]);
        });
    }

    [Fact]
    public async Task An_order_without_a_customer_still_tells_the_worker()
    {
        var notifier = new FakeNotificationService();

        await new CustomerAbsentApprovedNotificationHandler(new Reads(), notifier, NullLogger<CustomerAbsentApprovedNotificationHandler>.Instance)
            .Handle(Approved(), default);

        Assert.Equal(UserRole.Worker, Assert.Single(notifier.Sent).RecipientRole);
    }

    [Fact]
    public async Task A_failing_notifier_or_read_is_logged_and_never_thrown_so_the_committed_approval_stays_a_success()
    {
        var failing = new FailingNotifier();
        var reads = new Reads { Admins = { 3 }, Customers = { [900] = 11 } };

        await new CustomerAbsentReportedNotificationHandler(reads, failing, NullLogger<CustomerAbsentReportedNotificationHandler>.Instance).Handle(Reported(), default);
        await new CustomerAbsentApprovedNotificationHandler(reads, failing, NullLogger<CustomerAbsentApprovedNotificationHandler>.Instance).Handle(Approved(), default);
        await new CustomerAbsentApprovedNotificationHandler(new Reads { Throw = true }, new FakeNotificationService(), NullLogger<CustomerAbsentApprovedNotificationHandler>.Instance)
            .Handle(Approved(), default);

        Assert.Equal(3, failing.Calls); // one admin + the worker + the customer: each one tried even though the others failed
    }
}
