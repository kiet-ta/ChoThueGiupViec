using CommonService.Application.Common.Options;

namespace CommonService.Application.Features.Booking;

/// <param name="RequiredWorkers">1, or exactly 2 parallel Job Assignments for a large area (BR-02, decisions Q01: never 3+).</param>
/// <param name="MaxHoursPerShift">Upper bound of one worker's shift (BR-01, <c>Shift.MaxHours</c>).</param>
/// <param name="IsLargeArea">True when the area is over <c>Area.StandardMaxM2</c>.</param>
public sealed record ShiftPlan(int RequiredWorkers, int MaxHoursPerShift, bool IsLargeArea);

/// <summary>
/// Pure shift rule (PRD BR-01/BR-02, decisions Q01): area &lt;= <c>Area.StandardMaxM2</c> -> 1 worker,
/// above it -> 2 workers in parallel; each shift lasts at most <c>Shift.MaxHours</c>.
/// No DB, I/O or clock. The BR-02 fallback (one worker, two consecutive shifts) is Dispatch's (BE-M3-08).
/// </summary>
public static class ShiftPlanner
{
    private const int StandardWorkers = 1;
    private const int LargeAreaWorkers = 2;

    /// <param name="totalAreaM2">The address S_total (PRD §1.1), strictly positive.</param>
    public static ShiftPlan Plan(decimal totalAreaM2, BusinessRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (totalAreaM2 <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalAreaM2), totalAreaM2, "Area must be strictly positive.");
        }

        var isLargeArea = totalAreaM2 > rules.Area.StandardMaxM2;
        return new ShiftPlan(
            isLargeArea ? LargeAreaWorkers : StandardWorkers,
            rules.Shift.MaxHours,
            isLargeArea);
    }
}
