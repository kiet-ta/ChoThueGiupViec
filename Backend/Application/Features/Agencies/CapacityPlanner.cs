namespace CommonService.Application.Features.Agencies;

/// <summary>A free (AVAILABLE) roster slot of an eligible agency, as the planner sees it.</summary>
/// <param name="SlaScore">The agency's SLA score; a higher score is preferred between agencies of the same tier.</param>
/// <param name="PriorityDispatch">The agency's active package has priority dispatch (Pro, decisions Q08).</param>
/// <param name="CommissionRate">The commission rate of the agency's active package (decisions Q11).</param>
public record CapacitySlot(int SlotId, int AgencyId, decimal SlaScore, bool PriorityDispatch, decimal CommissionRate);

/// <summary>The slots one agency can staff an order with.</summary>
public record CapacityPlan(int AgencyId, decimal CommissionRate, IReadOnlyList<int> SlotIds);

/// <summary>
/// BE-M5-06a &amp; BE-AGENCY-FAIR-01: Pure capacity rule (no database, no clock).
/// Given the free slots of the eligible agencies, the one agency that can staff the whole request is chosen:
/// a single agency must cover every seat (a Premium order is one company's service).
/// Ranking order:
/// 1. Priority dispatch (Pro subscription) first
/// 2. Higher SLA score
/// 3. Tie-breaker between equal tier &amp; SLA:
///    - If orderId &gt; 0: Deterministic pseudo-random hashing via HashCode.Combine(AgencyId, orderId) to balance
///      distribution across orders fairly and avoid perpetual monopoly by the lowest AgencyId.
///    - If orderId &lt;= 0: Fallback to lower AgencyId for deterministic backwards compatibility.
/// 4. Worker slot selection: lowest SlotId (or hashed by orderId if orderId &gt; 0).
/// </summary>
public static class CapacityPlanner
{
    /// <summary>The package of an agency without an active subscription: FREE (decisions Q08), commission 0.200 (decisions Q11).</summary>
    public const decimal FreeCommissionRate = 0.200m;

    /// <param name="openSlots">Free slots of eligible agencies only (eligibility is the caller's job).</param>
    /// <param name="requiredWorkers">1, or 2 for a large area (decisions Q01).</param>
    /// <param name="excludedSlotIds">Slots to ignore, e.g. the ones another order won while planning.</param>
    /// <param name="orderId">The order ID for fair pseudo-random tie-breaking; 0 for legacy deterministic order.</param>
    /// <returns>The plan, or null when no agency has <paramref name="requiredWorkers"/> free slots ("fully booked", Q13).</returns>
    public static CapacityPlan? Plan(
        IEnumerable<CapacitySlot> openSlots,
        int requiredWorkers,
        IReadOnlySet<int>? excludedSlotIds = null,
        long orderId = 0)
    {
        ArgumentNullException.ThrowIfNull(openSlots);
        if (requiredWorkers <= 0)
        {
            return null;
        }

        var baseQuery = openSlots
            .Where(s => excludedSlotIds is null || !excludedSlotIds.Contains(s.SlotId))
            .DistinctBy(s => s.SlotId)
            .GroupBy(s => s.AgencyId)
            .Where(g => g.Count() >= requiredWorkers)
            .OrderByDescending(g => g.First().PriorityDispatch)
            .ThenByDescending(g => g.First().SlaScore);

        var ordered = orderId > 0
            ? baseQuery.ThenBy(g => HashCode.Combine(g.Key, orderId))
            : baseQuery.ThenBy(g => g.Key);

        var best = ordered.FirstOrDefault();

        if (best is null)
        {
            return null;
        }

        return new CapacityPlan(
            best.Key,
            best.First().CommissionRate,
            best.Select(s => s.SlotId).Order().Take(requiredWorkers).ToList());
    }

    /// <summary>True when some agency can staff the request.</summary>
    public static bool HasCapacity(IEnumerable<CapacitySlot> openSlots, int requiredWorkers) =>
        Plan(openSlots, requiredWorkers) is not null;
}
