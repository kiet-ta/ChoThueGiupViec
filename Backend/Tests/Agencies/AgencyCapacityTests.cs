using CommonService.Application.Common.Options;
using CommonService.Application.Features.Agencies;
using CommonService.Application.Interfaces.Ports;
using CommonService.Infrastructure.Fakes;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

namespace CommonService.Tests.Agencies;

/// <summary>
/// In-memory <see cref="ICapacityRepository"/> with the semantics of the EF one: a hold is all-or-nothing and a slot can be held by one
/// order only. <see cref="Eligible"/> stands for "ACTIVE agency with sla_score >= the minimum".
/// </summary>
internal sealed class InMemoryCapacityRepository : ICapacityRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<int, CapacitySlot> _slots = new();
    private readonly Dictionary<int, long> _heldBy = new();
    private readonly Dictionary<long, StoredCapacityHold> _holds = new();

    public int OpenSlotReads { get; private set; }

    public void AddSlot(int slotId, int agencyId, decimal slaScore = 100m, bool priority = false, decimal commission = 0.200m)
    {
        lock (_gate)
        {
            _slots[slotId] = new CapacitySlot(slotId, agencyId, slaScore, priority, commission);
        }
    }

    public bool IsHeld(int slotId)
    {
        lock (_gate)
        {
            return _heldBy.ContainsKey(slotId);
        }
    }

    /// <summary>A competing order wins this slot between the planner's read and the hold (the race).</summary>
    public Action<int>? BeforeHold { get; set; }

    public Task<IReadOnlyList<CapacitySlot>> GetOpenSlotsAsync(DateOnly date, string shiftCode, decimal minSlaScore, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            OpenSlotReads++;
            return Task.FromResult<IReadOnlyList<CapacitySlot>>(
                _slots.Values.Where(s => s.SlaScore >= minSlaScore && !_heldBy.ContainsKey(s.SlotId)).ToList());
        }
    }

    public Task<bool> TryHoldAsync(
        long orderId, Guid reservationId, int agencyId, IReadOnlyList<int> slotIds, decimal commissionRate, DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        BeforeHold?.Invoke(slotIds[0]);
        lock (_gate)
        {
            if (slotIds.Any(_heldBy.ContainsKey))
            {
                return Task.FromResult(false);
            }

            foreach (var slotId in slotIds)
            {
                _heldBy[slotId] = orderId;
            }

            _holds[orderId] = new StoredCapacityHold(reservationId, orderId, agencyId, slotIds.Order().ToList(), commissionRate);
            return Task.FromResult(true);
        }
    }

    public Task<StoredCapacityHold?> GetByOrderAsync(long orderId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_holds.TryGetValue(orderId, out var hold) ? hold : null);
        }
    }

    public Task ReleaseByOrderAsync(long orderId, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_holds.Remove(orderId, out var hold))
            {
                foreach (var slotId in hold.SlotIds)
                {
                    _heldBy.Remove(slotId);
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task ReleaseAsync(Guid reservationId, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        long? orderId;
        lock (_gate)
        {
            orderId = _holds.Values.Where(h => h.ReservationId == reservationId).Select(h => (long?)h.OrderId).FirstOrDefault();
        }

        return orderId is null ? Task.CompletedTask : ReleaseByOrderAsync(orderId.Value, nowUtc, cancellationToken);
    }
}

/// <summary>BE-M5-06a / BE-M5-06: the capacity rule and the real IAgencyCapacityService (Premium phase 1, booking.md B8).</summary>
public class AgencyCapacityTests
{
    private static readonly DateOnly Date = new(2026, 10, 15);
    private const string Shift = "SHIFT_MORNING";

    private static CapacityRequest Request(long orderId, int workers = 1) => new(Date, Shift, workers, orderId);

    private static (AgencyCapacityService Service, InMemoryCapacityRepository Repo) Create()
    {
        var repo = new InMemoryCapacityRepository();
        var service = new AgencyCapacityService(repo, new FakeClock(), MicrosoftOptions.Create(new BusinessRules()));
        return (service, repo);
    }

    // ---- the pure rule (06a)

    [Fact]
    public void Planner_needs_one_agency_to_cover_every_seat()
    {
        // Two agencies with one free slot each cannot staff a 2-seat order: a Premium order is one company's service.
        var slots = new[]
        {
            new CapacitySlot(1, AgencyId: 10, 100m, false, 0.2m),
            new CapacitySlot(2, AgencyId: 20, 100m, false, 0.2m),
        };

        Assert.Null(CapacityPlanner.Plan(slots, requiredWorkers: 2));
        Assert.NotNull(CapacityPlanner.Plan(slots, requiredWorkers: 1));
    }

    [Fact]
    public void Planner_takes_the_lowest_slot_ids_of_the_chosen_agency()
    {
        var slots = new[] { 9, 3, 5 }.Select(id => new CapacitySlot(id, 10, 100m, false, 0.2m));

        var plan = CapacityPlanner.Plan(slots, 2)!;

        Assert.Equal([3, 5], plan.SlotIds);
        Assert.Equal(10, plan.AgencyId);
    }

    [Fact]
    public void Planner_prefers_priority_dispatch_then_sla_then_lower_agency_id()
    {
        var pro = new CapacitySlot(1, AgencyId: 30, SlaScore: 60m, PriorityDispatch: true, CommissionRate: 0m);
        var freeHigh = new CapacitySlot(2, AgencyId: 20, SlaScore: 99m, PriorityDispatch: false, CommissionRate: 0.2m);
        var freeLow = new CapacitySlot(3, AgencyId: 10, SlaScore: 80m, PriorityDispatch: false, CommissionRate: 0.2m);

        Assert.Equal(30, CapacityPlanner.Plan([freeLow, freeHigh, pro], 1)!.AgencyId);
        Assert.Equal(20, CapacityPlanner.Plan([freeLow, freeHigh], 1)!.AgencyId);
        Assert.Equal(10, CapacityPlanner.Plan([freeLow with { SlaScore = 99m }, freeHigh], 1)!.AgencyId);
    }

    [Fact]
    public void Planner_skips_excluded_slots_and_ignores_a_non_positive_request()
    {
        var slots = new[] { new CapacitySlot(1, 10, 100m, false, 0.2m), new CapacitySlot(2, 10, 100m, false, 0.2m) };

        Assert.Equal([2], CapacityPlanner.Plan(slots, 1, new HashSet<int> { 1 })!.SlotIds);
        Assert.Null(CapacityPlanner.Plan(slots, 0));
        Assert.Null(CapacityPlanner.Plan([], 1));
    }

    [Fact]
    public void Planner_copies_the_package_commission_of_the_chosen_agency()
    {
        var plan = CapacityPlanner.Plan([new CapacitySlot(1, 10, 100m, true, CommissionRate: 0m)], 1)!;

        Assert.Equal(0m, plan.CommissionRate);
        Assert.Equal(0.200m, CapacityPlanner.FreeCommissionRate);
    }

    // ---- the service (06)

    [Fact]
    public async Task Reserve_holds_the_slots_and_reports_the_commission()
    {
        var (service, repo) = Create();
        repo.AddSlot(1, 10, commission: 0m);
        repo.AddSlot(2, 10, commission: 0m);

        var reservation = await service.TryReserveAsync(Request(501, workers: 2));

        Assert.NotNull(reservation);
        Assert.Equal(10, reservation.AgencyId);
        Assert.Equal([1, 2], reservation.SlotIds);
        Assert.Equal(0m, reservation.CommissionRate);
        Assert.True(repo.IsHeld(1) && repo.IsHeld(2));
        Assert.False(await service.HasCapacityAsync(Request(502)));
    }

    [Fact]
    public async Task Reserve_is_idempotent_per_order_and_readable_by_order()
    {
        var (service, repo) = Create();
        repo.AddSlot(1, 10);
        repo.AddSlot(2, 10);

        var first = await service.TryReserveAsync(Request(501));
        var again = await service.TryReserveAsync(Request(501));
        var read = await service.GetReservationByOrderAsync(501);

        Assert.Equal(first!.ReservationId, again!.ReservationId);
        Assert.Equal(first.ReservationId, read!.ReservationId);
        Assert.Null(await service.GetReservationByOrderAsync(999));
        Assert.False(repo.IsHeld(2)); // the retry did not take a second slot
    }

    [Fact]
    public async Task Fully_booked_returns_null_and_holds_nothing()
    {
        var (service, repo) = Create();
        repo.AddSlot(1, 10);

        var reservation = await service.TryReserveAsync(Request(501, workers: 2));

        Assert.Null(reservation);
        Assert.False(repo.IsHeld(1));
        Assert.False(await service.HasCapacityAsync(Request(501, workers: 2)));
    }

    [Fact]
    public async Task An_agency_below_the_sla_minimum_is_not_offered()
    {
        var (service, repo) = Create();
        repo.AddSlot(1, 10, slaScore: 49.99m);
        repo.AddSlot(2, 20, slaScore: 50m);

        var reservation = await service.TryReserveAsync(Request(501));

        Assert.Equal(20, reservation!.AgencyId); // Sla.BlockPremiumBelow = 50 (decisions Q25 R8)
    }

    [Fact]
    public async Task Release_by_order_and_by_reservation_free_the_slots_and_are_idempotent()
    {
        var (service, repo) = Create();
        repo.AddSlot(1, 10);
        repo.AddSlot(2, 10);
        var a = await service.TryReserveAsync(Request(501));
        var b = await service.TryReserveAsync(Request(502));

        await service.ReleaseByOrderAsync(501);
        await service.ReleaseByOrderAsync(501); // second call: no-op
        await service.ReleaseAsync(b!.ReservationId);
        await service.ReleaseAsync(Guid.NewGuid()); // unknown id: no-op

        Assert.False(repo.IsHeld(1) || repo.IsHeld(2));
        Assert.Null(await service.GetReservationByOrderAsync(501));
        Assert.NotEqual(a!.ReservationId, b.ReservationId);
    }

    [Fact]
    public async Task Lost_race_plans_again_on_the_next_free_slot()
    {
        var (service, repo) = Create();
        repo.AddSlot(1, 10);
        repo.AddSlot(2, 10);
        var stolen = false;
        repo.BeforeHold = _ =>
        {
            if (stolen)
            {
                return;
            }

            stolen = true;
            // Another customer takes slot 1 after this request planned on it.
            repo.TryHoldAsync(900, Guid.NewGuid(), 10, [1], 0.2m, DateTime.UtcNow).GetAwaiter().GetResult();
        };

        var reservation = await service.TryReserveAsync(Request(501));

        Assert.Equal([2], reservation!.SlotIds);
        Assert.True(repo.IsHeld(1));
    }

    [Fact]
    public async Task Two_customers_race_for_one_slot_and_exactly_one_wins()
    {
        var (service, repo) = Create();
        repo.AddSlot(1, 10);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(i => Task.Run(() => service.TryReserveAsync(Request(1000 + i)))));

        Assert.Equal(1, results.Count(r => r is not null));
        Assert.True(repo.IsHeld(1));
    }

    [Fact]
    public async Task Many_customers_never_hold_more_slots_than_exist()
    {
        var (service, repo) = Create();
        for (var i = 1; i <= 4; i++)
        {
            repo.AddSlot(i, 10);
        }

        var results = await Task.WhenAll(
            Enumerable.Range(0, 30).Select(i => Task.Run(() => service.TryReserveAsync(Request(2000 + i)))));

        var won = results.Where(r => r is not null).ToList();
        Assert.Equal(4, won.Count);
        Assert.Equal(4, won.SelectMany(r => r!.SlotIds).Distinct().Count());
    }
}
