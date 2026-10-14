namespace CommonService.Application.Features.Agencies;

/// <summary>A free (AVAILABLE) roster slot of an eligible agency, as the planner sees it.</summary>
/// <param name="SlaScore">The agency's SLA score; a higher score is preferred between agencies of the same tier.</param>
/// <param name="PriorityDispatch">The agency's active package has priority dispatch (Pro, decisions Q08).</param>
/// <param name="CommissionRate">The commission rate of the agency's active package (decisions Q11).</param>
public sealed record CapacitySlot(int SlotId, int AgencyId, decimal SlaScore, bool PriorityDispatch, decimal CommissionRate);

/// <summary>The slots one agency can staff an order with.</summary>
public sealed record CapacityPlan(int AgencyId, decimal CommissionRate, IReadOnlyList<int> SlotIds);

/// <summary>
/// BE-M5-06a, the pure capacity rule (no database, no clock). Given the free slots of the eligible agencies, the one agency that can
/// staff the whole request is chosen: a single agency must cover every seat (a Premium order is one company's service). Between agencies
/// that can: priority dispatch (Pro) first, then the higher SLA score, then the lower agency id, so the result is deterministic.
/// The agency's own slots are used lowest id first.
/// </summary>
public static class CapacityPlanner
{
    /// <summary>The package of an agency without an active subscription: FREE (decisions Q08), commission 0.200 (decisions Q11).</summary>
    public const decimal FreeCommissionRate = 0.200m;

    /// <param name="openSlots">Free slots of eligible agencies only (eligibility is the caller's job).</param>
    /// <param name="requiredWorkers">1, or 2 for a large area (decisions Q01).</param>
    /// <param name="excludedSlotIds">Slots to ignore, e.g. the ones another order won while planning.</param>
    /// <returns>The plan, or null when no agency has <paramref name="requiredWorkers"/> free slots ("fully booked", Q13).</returns>
    public static CapacityPlan? Plan(
        IEnumerable<CapacitySlot> openSlots,
        int requiredWorkers,
        IReadOnlySet<int>? excludedSlotIds = null)
    {
        ArgumentNullException.ThrowIfNull(openSlots);
        if (requiredWorkers <= 0)
        {
            return null;
        }

        var best = openSlots
            .Where(s => excludedSlotIds is null || !excludedSlotIds.Contains(s.SlotId))
            .DistinctBy(s => s.SlotId)
            .GroupBy(s => s.AgencyId)
            .Where(g => g.Count() >= requiredWorkers)
            .OrderByDescending(g => g.First().PriorityDispatch)
            .ThenByDescending(g => g.First().SlaScore)
            .ThenBy(g => g.Key)
            .FirstOrDefault();

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
