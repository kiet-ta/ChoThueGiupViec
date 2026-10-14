using CommonService.Application.Common.Options;
using CommonService.Application.Interfaces.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Agencies;

/// <summary>A reservation as stored: the seats of one order.</summary>
public sealed record StoredCapacityHold(Guid ReservationId, long OrderId, int AgencyId, IReadOnlyList<int> SlotIds, decimal CommissionRate);

/// <summary>
/// Persistence of Premium capacity holds. Every change is all-or-nothing and conditional, so two requests can never both win a slot.
/// Implemented with EF Core in Infrastructure; tests supply an in-memory one with the same semantics.
/// </summary>
public interface ICapacityRepository
{
    /// <summary>
    /// The free (<c>AVAILABLE</c>, not held) agency slots of the date and shift whose agency is <c>ACTIVE</c> with an SLA score of at least
    /// <paramref name="minSlaScore"/> (decisions Q25 R8), with the agency's active package rate and priority flag.
    /// </summary>
    Task<IReadOnlyList<CapacitySlot>> GetOpenSlotsAsync(DateOnly date, string shiftCode, decimal minSlaScore, CancellationToken cancellationToken = default);

    /// <summary>
    /// Holds all <paramref name="slotIds"/> for the order in one transaction: each slot moves <c>AVAILABLE -&gt; HELD</c> conditionally and a
    /// hold row is written. False (nothing changed) when any slot was no longer free.
    /// </summary>
    Task<bool> TryHoldAsync(
        long orderId, Guid reservationId, int agencyId, IReadOnlyList<int> slotIds, decimal commissionRate, DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task<StoredCapacityHold?> GetByOrderAsync(long orderId, CancellationToken cancellationToken = default);

    /// <summary>Deletes the order's hold rows and moves their still-<c>HELD</c> slots back to <c>AVAILABLE</c>. Idempotent.</summary>
    Task ReleaseByOrderAsync(long orderId, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Same for one reservation id. Idempotent.</summary>
    Task ReleaseAsync(Guid reservationId, DateTime nowUtc, CancellationToken cancellationToken = default);
}

/// <summary>
/// The real <see cref="IAgencyCapacityService"/> (BE-M5-06, Premium phase 1, contract booking.md B8): the hold is made when the order is
/// created, kept on payment and released when the order is cancelled or its QR expires. The reservation is keyed by the order, so
/// Booking stores nothing. Eligibility: ACTIVE agency with <c>sla_score &gt;= Sla.BlockPremiumBelow</c> (decisions Q25 R8, an ACTIVE agency
/// with an escrow warning still counts). No Skill matching: Skill was removed from the product (CONTEXT.md, 2026-10-13).
/// </summary>
public sealed class AgencyCapacityService(
    ICapacityRepository repository,
    IClock clock,
    IOptions<BusinessRules> rules,
    ILogger<AgencyCapacityService>? logger = null) : IAgencyCapacityService
{
    /// <summary>
    /// provenance: a lost race re-reads and plans again; with a handful of Premium agencies three rounds always find the next free slot
    /// or prove there is none, so the loop stays bounded without an arbitrary timeout.
    /// </summary>
    private const int MaxAttempts = 3;

    private readonly ILogger<AgencyCapacityService> _logger = logger ?? NullLogger<AgencyCapacityService>.Instance;

    private decimal MinSlaScore => rules.Value.Sla.BlockPremiumBelow;

    public async Task<bool> HasCapacityAsync(CapacityRequest request, CancellationToken cancellationToken = default)
    {
        var open = await repository.GetOpenSlotsAsync(request.Date, request.ShiftCode, MinSlaScore, cancellationToken);
        return CapacityPlanner.HasCapacity(open, request.RequiredWorkers);
    }

    public async Task<CapacityReservation?> TryReserveAsync(CapacityRequest request, CancellationToken cancellationToken = default)
    {
        // Idempotent per order: a repeated create (or a retry after a timeout) returns the hold that already exists.
        var existing = await repository.GetByOrderAsync(request.OrderId, cancellationToken);
        if (existing is not null)
        {
            return ToReservation(existing);
        }

        var lost = new HashSet<int>();
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var open = await repository.GetOpenSlotsAsync(request.Date, request.ShiftCode, MinSlaScore, cancellationToken);
            var plan = CapacityPlanner.Plan(open, request.RequiredWorkers, lost);
            if (plan is null)
            {
                return null; // fully booked (decisions Q13)
            }

            var reservationId = Guid.NewGuid();
            if (await repository.TryHoldAsync(
                    request.OrderId, reservationId, plan.AgencyId, plan.SlotIds, plan.CommissionRate, clock.UtcNow, cancellationToken))
            {
                return new CapacityReservation(reservationId, plan.AgencyId, plan.SlotIds, plan.CommissionRate);
            }

            _logger.LogInformation(
                "Order {OrderId}: lost a race for slots {Slots} of agency {AgencyId}; planning again.",
                request.OrderId, string.Join(",", plan.SlotIds), plan.AgencyId);
            lost.UnionWith(plan.SlotIds);
        }

        return null;
    }

    public async Task<CapacityReservation?> GetReservationByOrderAsync(long orderId, CancellationToken cancellationToken = default)
    {
        var hold = await repository.GetByOrderAsync(orderId, cancellationToken);
        return hold is null ? null : ToReservation(hold);
    }

    public Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken = default) =>
        repository.ReleaseAsync(reservationId, clock.UtcNow, cancellationToken);

    public Task ReleaseByOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
        repository.ReleaseByOrderAsync(orderId, clock.UtcNow, cancellationToken);

    private static CapacityReservation ToReservation(StoredCapacityHold hold) =>
        new(hold.ReservationId, hold.AgencyId, hold.SlotIds, hold.CommissionRate);
}
