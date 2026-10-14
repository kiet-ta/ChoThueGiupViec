using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Agencies;
using CommonService.Application.Features.Payments.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using MediatR;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

namespace CommonService.Tests.Agencies;

/// <summary>
/// BE-M5-10: acceptance of the Agencies module across its parts: the quota, the capacity race, the escrow that never goes negative, the
/// import errors by row and the Pro / Free feature access. The unit behaviour of each part is in its own test file; these tests make the
/// parts work together the way the API wires them.
/// </summary>
public class M5AcceptanceTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 10, 14);

    private sealed class RecordingPublisher : IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
    }

    private sealed class Wiring
    {
        public SubscriptionTests.InMemorySubscriptions Subscriptions { get; } = new();
        public SubscriptionTests.InMemoryPayments Payments { get; }
        public FakePaymentGateway Gateway { get; } = new();
        public FakeCurrentUser User { get; } = new();
        public FakeClock Clock { get; } = new();
        public BusinessRules Rules { get; } = new();
        public SubscriptionService Subscription { get; }
        public SubscriptionPaymentSettlement Settlement { get; }
        public WorkerImportTests.InMemoryImport Import { get; } = new() { GuaranteeSignedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) };
        public WorkerImportService Importer { get; }

        public Wiring()
        {
            Clock.Set(Now);
            Payments = new SubscriptionTests.InMemoryPayments(Subscriptions);
            var options = MicrosoftOptions.Create(Rules);
            Subscription = new SubscriptionService(Subscriptions, Payments, Gateway, Payments, new FakeAuditLog(), User, Clock, options);
            Settlement = new SubscriptionPaymentSettlement(Payments, Subscriptions, Payments, new RecordingPublisher(), Clock);
            Importer = new WorkerImportService(
                Import, Subscription, new FakeFileStorage(), new WorkerImportTests.PassThroughUnitOfWork(), User, Clock,
                NullLogger<WorkerImportService>.Instance);
            User.SignIn(7, UserRole.Partner);
        }

        public static byte[] File(int workers) => CommonService.Application.Features.Payouts.XlsxWriter.Build(
            "workers",
            WorkerImportService.Columns,
            Enumerable.Range(1, workers)
                .Select(i => (IReadOnlyList<object?>)new object?[] { $"Worker {i}", $"09{i:00000000}", $"{i:000000000000}", null, null })
                .ToList());
    }

    [Fact]
    public async Task A_free_agency_cannot_import_more_than_its_quota_but_a_pro_purchase_through_the_ipn_lifts_it()
    {
        var w = new Wiring();

        var rejected = await Assert.ThrowsAsync<ImportRejectedException>(
            () => w.Importer.CommitAsync(new MemoryStream(Wiring.File(5)))); // FREE: 3 workers
        Assert.Contains(rejected.Result.Errors, e => e.Code == ImportErrorCodes.QuotaExceeded);
        Assert.Empty(w.Import.Added);

        await w.Subscription.SubscribeAsync(new SubscribeRequest(2)); // PRO_MONTHLY
        var payment = w.Subscriptions.Payments.Single();
        Assert.True(await w.Settlement.SettleSuccessAsync(payment, false, "{}"));

        var committed = await w.Importer.CommitAsync(new MemoryStream(Wiring.File(5)));
        Assert.True(committed.Committed);
        Assert.Equal(5, w.Import.Added.Count);
    }

    [Fact]
    public async Task After_the_grace_days_the_import_quota_falls_back_to_free_again()
    {
        var w = new Wiring();
        w.Subscriptions.Subscriptions.Add(new PartnerSubscription
        {
            SubscriptionId = 50,
            AgencyId = 7,
            PackageId = 2,
            SubStatus = "ACTIVE",
            StartDate = Today.AddDays(-40),
            EndDate = Today.AddDays(-5),
        });

        var inGrace = await w.Importer.DryRunAsync(new MemoryStream(Wiring.File(10)));
        Assert.True(inGrace.IsValid); // ended 5 days ago, grace is 7 days: still Pro (50)

        w.Clock.Advance(TimeSpan.FromDays(3)); // 8 days after the end
        var afterGrace = await w.Importer.DryRunAsync(new MemoryStream(Wiring.File(10)));
        Assert.False(afterGrace.IsValid);
        Assert.Contains(afterGrace.Errors, e => e.Code == ImportErrorCodes.QuotaExceeded);
    }

    [Fact]
    public async Task Penalties_that_push_the_score_below_50_or_the_balance_to_zero_take_the_agency_out_of_premium_capacity()
    {
        var escrowRepo = new EscrowTests.InMemoryEscrow { Agency = new AgencyEscrowRow(7, 5_000_000m, 100m, "ACTIVE") };
        var escrowPayments = new EscrowTests.DepositPayments();
        var clock = new FakeClock();
        clock.Set(Now);
        var user = new FakeCurrentUser();
        user.SignIn(7, UserRole.Partner);
        var options = MicrosoftOptions.Create(new BusinessRules());
        var escrow = new EscrowService(escrowRepo, escrowPayments, new FakePaymentGateway(), escrowPayments, new FakeAuditLog(), user, clock, options);
        var capacity = new AgencyCapacityService(new EscrowBackedCapacity(escrowRepo), clock, options);

        Assert.True(await capacity.HasCapacityAsync(new CapacityRequest(Today, "SHIFT_MORNING", 1, 900)));

        // Three NO_SHOW penalties of one order each: 100 -> 40 points (below 50): excluded from Premium (decisions Q09, Q25 R8).
        for (var order = 1; order <= 3; order++)
        {
            await escrow.ApplyAsync(new SlaPenaltyRequest(7, SlaViolation.NoShow, order, null, 100_000m, 0m, "no show"));
        }

        Assert.Equal(40m, escrowRepo.Agency.SlaScore);
        Assert.Equal("ACTIVE", escrowRepo.Agency.Status);
        Assert.False(await capacity.HasCapacityAsync(new CapacityRequest(Today, "SHIFT_MORNING", 1, 901)));
        Assert.Null(await capacity.TryReserveAsync(new CapacityRequest(Today, "SHIFT_MORNING", 1, 902)));
    }

    [Fact]
    public async Task The_escrow_never_goes_negative_whatever_the_penalties()
    {
        var escrowRepo = new EscrowTests.InMemoryEscrow { Agency = new AgencyEscrowRow(7, 500_000m, 100m, "ACTIVE") };
        var escrowPayments = new EscrowTests.DepositPayments();
        var clock = new FakeClock();
        clock.Set(Now);
        var user = new FakeCurrentUser();
        user.SignIn(7, UserRole.Partner);
        var escrow = new EscrowService(
            escrowRepo, escrowPayments, new FakePaymentGateway(), escrowPayments, new FakeAuditLog(), user, clock,
            MicrosoftOptions.Create(new BusinessRules()));

        decimal shortfall = 0;
        for (var order = 1; order <= 5; order++)
        {
            var result = await escrow.ApplyAsync(new SlaPenaltyRequest(7, SlaViolation.Shortage, order, null, 300_000m, 50_000m, "short"));
            shortfall += result.Shortfall;
            Assert.True(escrowRepo.Agency.Balance >= 0m);
        }

        Assert.Equal(0m, escrowRepo.Agency.Balance);
        Assert.Equal("SUSPENDED", escrowRepo.Agency.Status);
        Assert.Equal((5 * 350_000m) - 500_000m, shortfall); // asked 1 750 000, had 500 000
        Assert.All(escrowRepo.Ledger, row => Assert.True(row.BalanceAfter >= 0m));
        Assert.Equal(5, escrowRepo.Ledger.Count);
    }

    [Fact]
    public async Task Pro_and_free_see_different_features_through_the_same_state_endpoint()
    {
        var w = new Wiring();
        var free = await w.Subscription.GetMineAsync();

        w.Subscriptions.Subscriptions.Add(new PartnerSubscription
        {
            SubscriptionId = 60,
            AgencyId = 7,
            PackageId = 2,
            SubStatus = "ACTIVE",
            StartDate = Today.AddDays(-1),
            EndDate = Today.AddDays(29),
        });
        var pro = await w.Subscription.GetMineAsync();

        Assert.Equal("FREE", free.Tier);
        Assert.False(free.HasRosterDashboard || free.HasAnalytics || free.PriorityDispatch);
        Assert.Equal("PRO", pro.Tier);
        Assert.True(pro.HasRosterDashboard && pro.HasAnalytics && pro.PriorityDispatch);
        Assert.True(pro.WorkerQuota > free.WorkerQuota);
    }

    /// <summary>Capacity candidates built from the escrow state: one slot of agency 7, eligible only while ACTIVE with a score of at least 50.</summary>
    private sealed class EscrowBackedCapacity(EscrowTests.InMemoryEscrow escrow) : ICapacityRepository
    {
        private readonly HashSet<long> _held = [];

        public Task<IReadOnlyList<CapacitySlot>> GetOpenSlotsAsync(DateOnly date, string shiftCode, decimal minSlaScore, CancellationToken cancellationToken = default)
        {
            var a = escrow.Agency;
            IReadOnlyList<CapacitySlot> slots = a.Status == "ACTIVE" && a.SlaScore >= minSlaScore && _held.Count == 0
                ? [new CapacitySlot(1, a.AgencyId, a.SlaScore, false, 0.2m)]
                : [];
            return Task.FromResult(slots);
        }

        public Task<bool> TryHoldAsync(
            long orderId, Guid reservationId, int agencyId, IReadOnlyList<int> slotIds, decimal commissionRate, DateTime nowUtc,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_held.Add(orderId));

        public Task<StoredCapacityHold?> GetByOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<StoredCapacityHold?>(null);

        public Task ReleaseByOrderAsync(long orderId, DateTime nowUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ReleaseAsync(Guid reservationId, DateTime nowUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
