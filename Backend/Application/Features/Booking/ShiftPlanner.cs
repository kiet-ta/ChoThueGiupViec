using CommonService.Application.Common.Options;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Booking;

/// <param name="RequiredWorkers">1, or exactly 2 parallel Job Assignments for a large area (BR-02, decisions Q01: never 3+).</param>
/// <param name="MaxHoursPerShift">Upper bound of one worker's shift (BR-01, <c>Shift.MaxHours</c>).</param>
/// <param name="IsLargeArea">True when the area is over <c>Area.StandardMaxM2</c>.</param>
/// <param name="PriceUnits">How many unit prices the order costs (decisions D36): 2 for a large area, whatever the number of workers.</param>
/// <param name="IsContinuousWork">ECONOMY large area (D36): ONE worker who may need a 2nd consecutive job; the customer must agree when booking.</param>
public record ShiftPlan(int RequiredWorkers, int MaxHoursPerShift, bool IsLargeArea, int PriceUnits, bool IsContinuousWork);

/// <summary>
/// Pure shift rule (PRD BR-01/BR-02, decisions Q01): area &lt;= <c>Area.StandardMaxM2</c> -> 1 worker,
/// above it -> 2 workers in parallel; each shift lasts at most <c>Shift.MaxHours</c>.
/// No DB, I/O or clock. The BR-02 fallback (one worker, two consecutive shifts) is Dispatch's (BE-M3-08).
/// D36: an ECONOMY large area is ONE worker (priced as 2 units) who may continue in the next shift; PREMIUM stays 2 parallel workers.
/// </summary>
public static class ShiftPlanner
{
    private const int StandardWorkers = 1;
    private const int LargeAreaWorkers = 2;

    /// <param name="totalAreaM2">The address S_total (PRD §1.1), strictly positive.</param>
    public static ShiftPlan Plan(decimal totalAreaM2, BusinessRules rules, ServiceTier serviceTier)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (totalAreaM2 <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalAreaM2), totalAreaM2, "Area must be strictly positive.");
        }

        var isLargeArea = totalAreaM2 > rules.Area.StandardMaxM2;
        var continuous = isLargeArea && serviceTier == ServiceTier.Economy;
        return new ShiftPlan(
            isLargeArea && !continuous ? LargeAreaWorkers : StandardWorkers,
            rules.Shift.MaxHours,
            isLargeArea,
            isLargeArea ? LargeAreaWorkers : StandardWorkers,
            continuous);
    }
}
