using CommonService.Application.Common.Options;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Dispatch;

/// <summary>One live seat of a Premium order, as the shift-start rule sees it.</summary>
/// <param name="IsReplacement">The seat was taken over by a rescue (an earlier assignment of the same seat was reassigned or cancelled).</param>
/// <param name="PendingCheckInAt">The worker tried a GPS check-in that failed and waits for the customer's confirmation or the plate photo; null otherwise.</param>
public record PremiumSeat(
    byte Seq, long AssignmentId, int WorkerId, int? AgencyId, JobAssignmentStatus Status, decimal GrossAmount,
    DateTime CreatedAt, bool IsReplacement, DateTime? PendingCheckInAt);

/// <summary>
/// Decision D38 (PREM-START-01): the pure rule of who is absent at the start of a Premium shift. No database, no clock of its own.
/// A seat is present when its assignment is <c>CHECKED_IN</c> or later. A seat that is not present is absent once its deadline passed:
/// the shift start + <see cref="PremiumRules.CheckInGraceMinutes"/>; a replacement seat has <see cref="PremiumRules.ReplacementCheckInMinutes"/>
/// from its assignment if that is later; a failed GPS check-in that waits for confirmation counts as present for
/// <see cref="PremiumRules.FallbackConfirmGraceMinutes"/> after the attempt. A seat with no live row counts as absent after the base deadline.
/// </summary>
public static class PremiumShiftStartRules
{
    private static readonly HashSet<JobAssignmentStatus> Present =
        [JobAssignmentStatus.CheckedIn, JobAssignmentStatus.InProgress, JobAssignmentStatus.AwaitingAcceptance, JobAssignmentStatus.Completed];

    /// <summary>The instant after which the seat counts as absent while it is not present.</summary>
    public static DateTime Deadline(PremiumSeat seat, DateTime shiftStartUtc, PremiumRules rules)
    {
        ArgumentNullException.ThrowIfNull(seat);
        ArgumentNullException.ThrowIfNull(rules);
        var deadline = shiftStartUtc.AddMinutes(rules.CheckInGraceMinutes);
        if (seat.IsReplacement)
        {
            var replacement = seat.CreatedAt.AddMinutes(rules.ReplacementCheckInMinutes);
            if (replacement > deadline)
            {
                deadline = replacement;
            }
        }

        if (seat.PendingCheckInAt is { } attempt)
        {
            var pending = attempt.AddMinutes(rules.FallbackConfirmGraceMinutes);
            if (pending > deadline)
            {
                deadline = pending;
            }
        }

        return deadline;
    }

    public static bool IsPresent(PremiumSeat seat) => Present.Contains(seat.Status);

    /// <summary>The seats that are absent now, and how many seats of the order have no live row at all (also absent once the base deadline passed).</summary>
    public static (IReadOnlyList<PremiumSeat> Absent, int Unfilled) Evaluate(
        IReadOnlyList<PremiumSeat> liveSeats, int requiredWorkers, DateTime shiftStartUtc, DateTime nowUtc, PremiumRules rules)
    {
        ArgumentNullException.ThrowIfNull(liveSeats);
        var absent = liveSeats
            .Where(s => !IsPresent(s) && nowUtc >= Deadline(s, shiftStartUtc, rules))
            .ToList();
        var unfilled = Math.Max(0, requiredWorkers - liveSeats.Select(s => s.Seq).Distinct().Count());
        if (nowUtc < shiftStartUtc.AddMinutes(rules.CheckInGraceMinutes))
        {
            unfilled = 0;
        }

        return (absent, unfilled);
    }
}
