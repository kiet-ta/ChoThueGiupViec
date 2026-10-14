using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Agencies;
using CommonService.Application.Features.Payments.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using CommonService.Infrastructure.Fakes;
using CommonService.Tests.Payments;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

namespace CommonService.Tests.Agencies;

/// <summary>BE-M5-02: subscription packages, purchase through the gateway, activation by the IPN, quota and grace (decisions Q08, Q11).</summary>
public class SubscriptionTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc); // 2026-10-14 10:00 Vietnam time
    private static readonly DateOnly Today = new(2026, 10, 14);

    private static SubscriptionPackage Free() => new()
    {
        PackageId = 1,
        PackageCode = "FREE",
        PackageName = "Free",
        Tier = "FREE",
        BillingCycle = "",
        Price = 0,
        WorkerQuota = 3,
        CommissionRate = 0.200m,
        IsActive = true,
    };

    private static SubscriptionPackage ProMonthly() => new()
    {
        PackageId = 2,
        PackageCode = "PRO_MONTHLY",
        PackageName = "Pro monthly",
        Tier = "PRO",
        BillingCycle = "MONTHLY",
        Price = 2_000_000,
        WorkerQuota = 50,
        CommissionRate = 0m,
        HasRosterDashboard = true,
        HasAnalytics = true,
        PriorityDispatch = true,
        IsActive = true,
    };

    private static SubscriptionPackage ProQuarterly() => new()
    {
        PackageId = 3,
        PackageCode = "PRO_QUARTERLY",
        PackageName = "Pro quarterly",
        Tier = "PRO",
        BillingCycle = "QUARTERLY",
        Price = 5_400_000,
        WorkerQuota = 50,
        CommissionRate = 0m,
        HasRosterDashboard = true,
        HasAnalytics = true,
        PriorityDispatch = true,
        IsActive = true,
    };

    private static PartnerSubscription Sub(int id, int packageId, string status, DateOnly start, DateOnly end, int agencyId = 7) => new()
    {
        SubscriptionId = id,
        AgencyId = agencyId,
        PackageId = packageId,
        SubStatus = status,
        StartDate = start,
        EndDate = end,
    };

    // ---- the pure rules

    [Fact]
    public void Running_subscription_is_in_force_and_the_grace_days_keep_pro_for_seven_days()
    {
        var pro = ProMonthly();
        var running = Sub(1, 2, "ACTIVE", Today.AddDays(-10), Today.AddDays(5));
        var ended3DaysAgo = Sub(2, 2, "ACTIVE", Today.AddDays(-40), Today.AddDays(-3));
        var ended7DaysAgo = Sub(3, 2, "ACTIVE", Today.AddDays(-40), Today.AddDays(-7));
        var ended8DaysAgo = Sub(4, 2, "ACTIVE", Today.AddDays(-40), Today.AddDays(-8));

        Assert.False(WorkerQuotaPolicy.Effective([(running, pro)], Free(), Today, 7).InGrace);
        var grace = WorkerQuotaPolicy.Effective([(ended3DaysAgo, pro)], Free(), Today, 7);
        Assert.True(grace.InGrace);
        Assert.Equal("PRO", grace.Package.Tier);
        Assert.Equal("PRO", WorkerQuotaPolicy.Effective([(ended7DaysAgo, pro)], Free(), Today, 7).Package.Tier);
        Assert.Equal("FREE", WorkerQuotaPolicy.Effective([(ended8DaysAgo, pro)], Free(), Today, 7).Package.Tier);
    }

    [Fact]
    public void Only_active_subscriptions_count_and_the_newest_wins()
    {
        var pending = Sub(1, 2, "PENDING_PAYMENT", Today, Today);
        var expired = Sub(2, 2, "EXPIRED", Today.AddDays(-60), Today.AddDays(-30));
        var old = Sub(3, 2, "ACTIVE", Today.AddDays(-20), Today.AddDays(1));
        var newer = Sub(4, 3, "ACTIVE", Today.AddDays(2), Today.AddDays(90));

        Assert.Equal("FREE", WorkerQuotaPolicy.Effective([(pending, ProMonthly()), (expired, ProMonthly())], Free(), Today, 7).Package.Tier);
        var effective = WorkerQuotaPolicy.Effective([(old, ProMonthly()), (newer, ProQuarterly())], Free(), Today, 7);
        Assert.Equal(2, effective.Package.PackageId); // the future-start one is not in force yet: the old one is
    }

    [Fact]
    public void Workers_over_quota_are_the_most_recently_created_first()
    {
        var workers = new[]
        {
            (1, new DateTime(2026, 1, 1)), (2, new DateTime(2026, 3, 1)), (3, new DateTime(2026, 2, 1)),
            (4, new DateTime(2026, 5, 1)), (5, new DateTime(2026, 4, 1)),
        };

        Assert.Equal([4, 5], WorkerQuotaPolicy.WorkersOverQuota(workers, 3));
        Assert.Empty(WorkerQuotaPolicy.WorkersOverQuota(workers, 5));
        Assert.Equal(5, WorkerQuotaPolicy.WorkersOverQuota(workers, 0).Count);
    }

    [Fact]
    public void A_renewal_starts_the_day_after_the_running_one_and_a_month_is_one_calendar_month()
    {
        Assert.Equal(Today, WorkerQuotaPolicy.NextStart(Today, null));
        Assert.Equal(Today, WorkerQuotaPolicy.NextStart(Today, Today.AddDays(-1)));
        Assert.Equal(Today.AddDays(6), WorkerQuotaPolicy.NextStart(Today, Today.AddDays(5)));
        Assert.Equal(new DateOnly(2026, 11, 13), WorkerQuotaPolicy.EndFor(Today, "MONTHLY"));
        Assert.Equal(new DateOnly(2027, 1, 13), WorkerQuotaPolicy.EndFor(Today, "QUARTERLY"));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkerQuotaPolicy.EndFor(Today, "YEARLY"));
    }

    // ---- the service

    private sealed class InMemorySubscriptions : ISubscriptionRepository
    {
        public List<SubscriptionPackage> Packages { get; } = [Free(), ProMonthly(), ProQuarterly()];
        public List<PartnerSubscription> Subscriptions { get; } = [];
        public Dictionary<int, string> AgencyStatus { get; } = new() { [7] = "ACTIVE" };
        public List<(int WorkerId, int AgencyId, DateTime CreatedAt, WorkStatus Status)> Workers { get; } = [];
        public List<PaymentTransaction> Payments { get; } = [];
        private int _nextSubscriptionId = 100;

        public Task<IReadOnlyList<SubscriptionPackage>> ListPackagesAsync(bool activeOnly, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionPackage>>(Packages.Where(p => !activeOnly || p.IsActive).OrderBy(p => p.Price).ToList());

        public Task<SubscriptionPackage?> GetPackageAsync(int packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Packages.FirstOrDefault(p => p.PackageId == packageId));

        public Task<SubscriptionPackage?> GetPackageForUpdateAsync(int packageId, CancellationToken cancellationToken = default) =>
            GetPackageAsync(packageId, cancellationToken);

        public Task<SubscriptionPackage?> GetPackageByCodeAsync(string packageCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Packages.FirstOrDefault(p => p.PackageCode == packageCode));

        public Task<string?> GetAgencyStatusAsync(int agencyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(AgencyStatus.TryGetValue(agencyId, out var s) ? s : null);

        public Task<IReadOnlyList<(PartnerSubscription Subscription, SubscriptionPackage Package)>> ListSubscriptionsAsync(
            int agencyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<(PartnerSubscription, SubscriptionPackage)>>(Subscriptions
                .Where(s => s.AgencyId == agencyId)
                .Select(s => (s, Packages.First(p => p.PackageId == s.PackageId)))
                .ToList());

        public Task<PartnerSubscription?> FindPendingAsync(int agencyId, int packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Subscriptions.LastOrDefault(s => s.AgencyId == agencyId && s.PackageId == packageId && s.SubStatus == "PENDING_PAYMENT"));

        public void Add(PartnerSubscription subscription)
        {
            subscription.SubscriptionId = _nextSubscriptionId++;
            Subscriptions.Add(subscription);
        }

        public Task<PartnerSubscription?> GetForUpdateAsync(int subscriptionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Subscriptions.FirstOrDefault(s => s.SubscriptionId == subscriptionId));

        public Task<IReadOnlyList<(int WorkerId, DateTime CreatedAt)>> ListCountedWorkersAsync(int agencyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<(int, DateTime)>>(Workers
                .Where(w => w.AgencyId == agencyId && w.Status != WorkStatus.Locked).Select(w => (w.WorkerId, w.CreatedAt)).ToList());

        public Task<IReadOnlyList<int>> ListAgenciesWithWorkersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<int>>(Workers.Where(w => w.Status != WorkStatus.Locked).Select(w => w.AgencyId).Distinct().ToList());

        public Task<int> LockWorkersAsync(IReadOnlyCollection<int> workerIds, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            var n = 0;
            for (var i = 0; i < Workers.Count; i++)
            {
                var w = Workers[i];
                if (workerIds.Contains(w.WorkerId) && w.Status is not (WorkStatus.Locked or WorkStatus.Busy))
                {
                    Workers[i] = w with { Status = WorkStatus.Locked };
                    n++;
                }
            }

            return Task.FromResult(n);
        }

        public Task<PaymentTransaction?> FindPendingPaymentAsync(int subscriptionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Payments.LastOrDefault(p => p.SubscriptionId == subscriptionId && p.TxnStatus == PaymentStatus.Pending));
    }

    private sealed class InMemoryPayments : PaymentRepositoryStub
    {
        private readonly InMemorySubscriptions _subs;
        private long _nextId = 900;

        public InMemoryPayments(InMemorySubscriptions subs) => _subs = subs;

        public override void Add(PaymentTransaction transaction)
        {
            transaction.PaymentId = _nextId++;
            _subs.Payments.Add(transaction);
        }

        public override Task<bool> TryMarkSuccessAsync(long paymentId, DateTime paidAtUtc, string ipnPayload, CancellationToken cancellationToken = default) =>
            Mark(paymentId, PaymentStatus.Pending);

        public override Task<bool> TryMarkSuccessFromExpiredAsync(long paymentId, DateTime paidAtUtc, string ipnPayload, CancellationToken cancellationToken = default) =>
            Mark(paymentId, PaymentStatus.Expired);

        private Task<bool> Mark(long paymentId, PaymentStatus from)
        {
            var t = _subs.Payments.First(p => p.PaymentId == paymentId);
            if (t.TxnStatus != from)
            {
                return Task.FromResult(false);
            }

            t.TxnStatus = PaymentStatus.Success;
            return Task.FromResult(true);
        }
    }

    private sealed class RecordingPublisher : IPublisher
    {
        public List<object> Published { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            Published.Add(notification);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification
        {
            Published.Add(notification!);
            return Task.CompletedTask;
        }
    }

    private sealed class Harness
    {
        public InMemorySubscriptions Repo { get; } = new();
        public InMemoryPayments Payments { get; }
        public FakePaymentGateway Gateway { get; } = new();
        public FakeAuditLog Audit { get; } = new();
        public FakeCurrentUser User { get; } = new();
        public FakeClock Clock { get; } = new();
        public RecordingPublisher Publisher { get; } = new();
        public SubscriptionService Service { get; }
        public SubscriptionPaymentSettlement Settlement { get; }
        public BusinessRules Rules { get; } = new();

        public Harness()
        {
            Clock.Set(Now);
            Payments = new InMemoryPayments(Repo);
            var options = MicrosoftOptions.Create(Rules);
            Service = new SubscriptionService(Repo, Payments, Gateway, Payments, Audit, User, Clock, options);
            Settlement = new SubscriptionPaymentSettlement(Payments, Repo, Payments, Publisher, Clock);
            User.SignIn(7, UserRole.Partner);
        }

        public void AddWorkers(int count, int agencyId = 7, WorkStatus status = WorkStatus.Idle)
        {
            for (var i = 0; i < count; i++)
            {
                Repo.Workers.Add((Repo.Workers.Count + 1, agencyId, new DateTime(2026, 1, 1).AddDays(Repo.Workers.Count), status));
            }
        }
    }

    [Fact]
    public async Task Packages_lists_the_active_ones_with_the_contract_fields()
    {
        var h = new Harness();
        h.Repo.Packages.Add(new SubscriptionPackage { PackageId = 9, PackageCode = "OLD", IsActive = false });

        var list = await h.Service.GetPackagesAsync();

        Assert.Equal(["FREE", "PRO_MONTHLY", "PRO_QUARTERLY"], list.Select(p => p.PackageCode));
        Assert.Equal(0.200m, list[0].CommissionRate);
        Assert.True(list[1].PriorityDispatch);
    }

    [Fact]
    public async Task State_without_a_subscription_is_the_free_package_with_its_quota()
    {
        var h = new Harness();
        h.AddWorkers(2);

        var state = await h.Service.GetMineAsync();

        Assert.Equal("FREE", state.Tier);
        Assert.Equal(3, state.WorkerQuota);
        Assert.Equal(2, state.WorkerCount);
        Assert.False(state.HasRosterDashboard);
        Assert.Null(state.Subscription);
    }

    [Fact]
    public async Task Subscribe_creates_a_pending_subscription_and_a_qr_and_repeats_return_the_same_qr()
    {
        var h = new Harness();

        var first = await h.Service.SubscribeAsync(new SubscribeRequest(2));
        var again = await h.Service.SubscribeAsync(new SubscribeRequest(2));

        Assert.Equal("PENDING_PAYMENT", first.Subscription.SubStatus);
        Assert.Equal(2_000_000m, first.Amount);
        Assert.NotNull(first.QrPayload);
        Assert.Equal(first.Subscription.SubscriptionId, again.Subscription.SubscriptionId);
        Assert.Equal(first.GatewayTxnRef, again.GatewayTxnRef);
        Assert.Single(h.Gateway.Created);
        Assert.Equal(PaymentPurpose.Subscription, h.Gateway.Created.Single().Purpose);
        var payment = Assert.Single(h.Repo.Payments);
        Assert.Equal(first.Subscription.SubscriptionId, payment.SubscriptionId);
        Assert.Equal(PaymentPurpose.Subscription, payment.Purpose);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public async Task Subscribe_needs_a_package_id(int? packageId)
    {
        var h = new Harness();

        await Assert.ThrowsAsync<ValidationException>(() => h.Service.SubscribeAsync(new SubscribeRequest(packageId)));
    }

    [Fact]
    public async Task Subscribe_refuses_the_free_package_an_unknown_package_and_a_suspended_agency()
    {
        var h = new Harness();

        var free = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => h.Service.SubscribeAsync(new SubscribeRequest(1)));
        Assert.Equal(SubscriptionErrorCodes.PackageNotPurchasable, free.Code);
        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.SubscribeAsync(new SubscribeRequest(99)));

        h.Repo.AgencyStatus[7] = "SUSPENDED";
        var suspended = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => h.Service.SubscribeAsync(new SubscribeRequest(2)));
        Assert.Equal(SubscriptionErrorCodes.AgencySuspended, suspended.Code);
    }

    [Fact]
    public async Task The_ipn_activates_the_subscription_once_and_publishes_SubscriptionActivated()
    {
        var h = new Harness();
        var purchase = await h.Service.SubscribeAsync(new SubscribeRequest(2));
        var payment = h.Repo.Payments.Single();

        var first = await h.Settlement.SettleSuccessAsync(payment, wasExpired: false, "{}");
        var repeat = await h.Settlement.SettleSuccessAsync(payment, wasExpired: false, "{}");

        Assert.True(first);
        Assert.False(repeat);
        var sub = h.Repo.Subscriptions.Single(s => s.SubscriptionId == purchase.Subscription.SubscriptionId);
        Assert.Equal("ACTIVE", sub.SubStatus);
        Assert.Equal(Today, sub.StartDate);
        Assert.Equal(new DateOnly(2026, 11, 13), sub.EndDate);
        var evt = Assert.IsType<SubscriptionActivated>(Assert.Single(h.Publisher.Published));
        Assert.Equal("PRO_MONTHLY", evt.PackageCode);
        Assert.Equal(7, evt.AgencyId);

        var state = await h.Service.GetMineAsync();
        Assert.Equal("PRO", state.Tier);
        Assert.Equal(50, state.WorkerQuota);
        Assert.True(state.HasRosterDashboard);
    }

    [Fact]
    public async Task A_payment_that_arrives_after_the_qr_expired_still_activates()
    {
        var h = new Harness();
        await h.Service.SubscribeAsync(new SubscribeRequest(2));
        var payment = h.Repo.Payments.Single();
        payment.TxnStatus = PaymentStatus.Expired;

        Assert.True(await h.Settlement.SettleSuccessAsync(payment, wasExpired: true, "{}"));
        Assert.Equal("ACTIVE", h.Repo.Subscriptions.Single().SubStatus);
    }

    [Fact]
    public async Task A_renewal_starts_after_the_running_subscription_so_no_day_is_lost()
    {
        var h = new Harness();
        h.Repo.Subscriptions.Add(Sub(50, 2, "ACTIVE", Today.AddDays(-20), Today.AddDays(10)));
        await h.Service.SubscribeAsync(new SubscribeRequest(3));

        await h.Settlement.SettleSuccessAsync(h.Repo.Payments.Single(), false, "{}");

        var renewed = h.Repo.Subscriptions.Single(s => s.PackageId == 3);
        Assert.Equal(Today.AddDays(11), renewed.StartDate);
        Assert.Equal(new DateOnly(2027, 1, 24), renewed.EndDate);
    }

    [Fact]
    public async Task Quota_guard_blocks_going_over_the_quota_in_force()
    {
        var h = new Harness();
        h.AddWorkers(2);

        await h.Service.EnsureCanAddWorkersAsync(7, 1); // 3 of 3
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => h.Service.EnsureCanAddWorkersAsync(7, 2));
        Assert.Equal(SubscriptionErrorCodes.QuotaExceeded, ex.Code);

        h.Repo.Subscriptions.Add(Sub(50, 2, "ACTIVE", Today.AddDays(-1), Today.AddDays(29)));
        await h.Service.EnsureCanAddWorkersAsync(7, 48); // Pro: 50
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => h.Service.EnsureCanAddWorkersAsync(7, 49));
    }

    [Fact]
    public async Task Admin_edit_writes_one_audit_row_per_changed_field_and_needs_a_reason()
    {
        var h = new Harness();
        h.User.SignIn(1, UserRole.Admin);

        var edited = await h.Service.UpdatePackageAsync(2, new UpdatePackageRequest(2_500_000m, 60, null, "Q4 pricing"));

        Assert.Equal(2_500_000m, edited.Price);
        Assert.Equal(60, edited.WorkerQuota);
        Assert.Equal(2, h.Audit.Entries.Count);
        Assert.All(h.Audit.Entries, e =>
        {
            Assert.Equal("SUBSCRIPTION_PACKAGE", e.EntityType);
            Assert.Equal("Q4 pricing", e.Reason);
            Assert.Equal(1, e.AdminId);
        });
        Assert.Contains(h.Audit.Entries, e => e.FieldName == "price" && e.OldValue == "2000000" && e.NewValue == "2500000");

        await Assert.ThrowsAsync<ValidationException>(() => h.Service.UpdatePackageAsync(2, new UpdatePackageRequest(1m, null, null, " ")));
        await Assert.ThrowsAsync<ValidationException>(() => h.Service.UpdatePackageAsync(2, new UpdatePackageRequest(null, null, null, "x")));
        await Assert.ThrowsAsync<ValidationException>(() => h.Service.UpdatePackageAsync(2, new UpdatePackageRequest(null, null, 1.5m, "x")));
        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.UpdatePackageAsync(99, new UpdatePackageRequest(1m, null, null, "x")));
    }

    [Fact]
    public async Task An_unchanged_value_writes_no_audit_row()
    {
        var h = new Harness();
        h.User.SignIn(1, UserRole.Admin);

        await h.Service.UpdatePackageAsync(2, new UpdatePackageRequest(2_000_000m, null, null, "same"));

        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task A_partner_cannot_edit_packages_and_an_admin_cannot_buy()
    {
        var h = new Harness();

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => h.Service.UpdatePackageAsync(2, new UpdatePackageRequest(1m, null, null, "x")));
        h.User.SignIn(1, UserRole.Admin);
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => h.Service.SubscribeAsync(new SubscribeRequest(2)));
    }

    [Fact]
    public async Task After_the_grace_days_the_newest_workers_over_the_free_quota_are_locked_but_a_worker_on_site_waits()
    {
        var h = new Harness();
        h.Repo.Subscriptions.Add(Sub(50, 2, "ACTIVE", Today.AddDays(-60), Today.AddDays(-8))); // ended 8 days ago: grace is over
        h.AddWorkers(5); // quota 3 -> workers 4 and 5 (the newest) are over
        h.Repo.Workers[4] = h.Repo.Workers[4] with { Status = WorkStatus.Busy };
        var enforcer = new WorkerQuotaEnforcer(h.Service, h.Repo, h.Payments, h.Clock, NullLogger<WorkerQuotaEnforcer>.Instance);

        var locked = await enforcer.EnforceAsync();

        Assert.Equal(1, locked);
        Assert.Equal(WorkStatus.Locked, h.Repo.Workers[3].Status);
        Assert.Equal(WorkStatus.Busy, h.Repo.Workers[4].Status);
        Assert.Equal(3, h.Repo.Workers.Count(w => w.Status is WorkStatus.Idle));
    }

    [Fact]
    public async Task Inside_the_grace_days_nobody_is_locked()
    {
        var h = new Harness();
        h.Repo.Subscriptions.Add(Sub(50, 2, "ACTIVE", Today.AddDays(-40), Today.AddDays(-5)));
        h.AddWorkers(10);
        var enforcer = new WorkerQuotaEnforcer(h.Service, h.Repo, h.Payments, h.Clock, NullLogger<WorkerQuotaEnforcer>.Instance);

        Assert.Equal(0, await enforcer.EnforceAsync());
    }

    // ---- the IPN routes a subscription payment to the handler

    [Fact]
    public async Task The_ipn_endpoint_routes_a_subscription_payment_to_its_handler_and_repeats_are_no_ops()
    {
        var h = new Harness();
        await h.Service.SubscribeAsync(new SubscribeRequest(2));
        var payment = h.Repo.Payments.Single();
        var ipn = new IpnService(
            h.Gateway, new IpnPayments(h.Repo, h.Payments), new NoSettlement(), NullLogger<IpnService>.Instance, [h.Settlement]);
        var payload = new Dictionary<string, string>
        {
            ["signature"] = "valid",
            ["gatewayTxnRef"] = payment.GatewayTxnRef,
            ["amount"] = "2000000",
            ["status"] = "success",
        };

        var first = await ipn.HandleAsync(payload, "{}");
        var repeat = await ipn.HandleAsync(payload, "{}");

        Assert.Equal(IpnOutcome.Accepted, first);
        Assert.Equal(IpnOutcome.AlreadyProcessed, repeat);
        Assert.Equal("ACTIVE", h.Repo.Subscriptions.Single().SubStatus);
    }

    [Fact]
    public async Task The_ipn_endpoint_still_rejects_a_purpose_nobody_handles()
    {
        var h = new Harness();
        await h.Service.SubscribeAsync(new SubscribeRequest(2));
        var payment = h.Repo.Payments.Single();
        var ipn = new IpnService(h.Gateway, new IpnPayments(h.Repo, h.Payments), new NoSettlement(), NullLogger<IpnService>.Instance);
        var payload = new Dictionary<string, string>
        {
            ["signature"] = "valid",
            ["gatewayTxnRef"] = payment.GatewayTxnRef,
            ["amount"] = "2000000",
            ["status"] = "success",
        };

        Assert.Equal(IpnOutcome.Rejected, await ipn.HandleAsync(payload, "{}"));
        Assert.Equal(PaymentStatus.Pending, payment.TxnStatus);
    }

    private sealed class IpnPayments(InMemorySubscriptions subs, InMemoryPayments inner) : PaymentRepositoryStub
    {
        public override Task<PaymentTransaction?> FindByGatewayRefAsync(string gatewayTxnRef, CancellationToken cancellationToken = default) =>
            Task.FromResult(subs.Payments.FirstOrDefault(p => p.GatewayTxnRef == gatewayTxnRef));

        public override Task<bool> TryMarkExpiredAsync(long paymentId, string ipnPayload, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class NoSettlement : IPaymentSettlementService
    {
        public Task<bool> SettlePaidAsync(long paymentId, long orderId, decimal amount, string rawBody, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> SettleExtensionPaidAsync(long paymentId, int extensionId, string rawBody, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> SettleLatePaymentAsync(
            long paymentId, PaymentPurpose purpose, long? orderId, int? extensionId, decimal amount, string rawBody, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
