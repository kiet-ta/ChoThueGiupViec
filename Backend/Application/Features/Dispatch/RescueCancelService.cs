using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Booking;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Dispatch;

/// <summary>
/// Decision D38 (PREM-RESCUE-01): a Super-Freelancer who was given a rescue seat (a PREMIUM assignment with no agency, taken over from another seat)
/// may cancel it without any penalty until its check-in deadline. The order is then cancelled (100 % refund, apology) and the colleague goes back
/// to the agency. The assignment ends as <c>CANCELLED</c>, not <c>CANCELLED_BY_WORKER</c>, so the lock count of Q15 never sees it.
/// </summary>
public class RescueCancelService(
    IPremiumStartRepository repository,
    PremiumOrderCanceller canceller,
    IClock clock,
    IOptions<BusinessRules> rules)
{
    public const string Reason = "Thợ cứu hộ không thể đến kịp (đơn Premium).";

    /// <summary>A rescue seat: PREMIUM, a freelancer (no agency); an agency seat never reaches the freelancer cancel call.</summary>
    public static bool IsRescue(JobAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        return assignment.ServiceTier == ServiceTier.Premium && assignment.AgencyId is null;
    }

    /// <summary>The instant the rescue worker must be present by: <c>max(assigned + Premium.ReplacementCheckInMinutes, start + Premium.CheckInGraceMinutes)</c>.</summary>
    public DateTime Deadline(JobAssignment assignment, DateTime shiftStartUtc)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        var premium = rules.Value.Premium;
        var assigned = assignment.CreatedAt.AddMinutes(premium.ReplacementCheckInMinutes);
        var usual = shiftStartUtc.AddMinutes(premium.CheckInGraceMinutes);
        return assigned > usual ? assigned : usual;
    }

    /// <summary>409 once the check-in deadline passed (the shift-start sweep decides from then on).</summary>
    public void RequireBeforeDeadline(JobAssignment assignment, DateTime shiftStartUtc)
    {
        if (clock.UtcNow >= Deadline(assignment, shiftStartUtc))
        {
            throw new BusinessRuleViolationException("Đã quá hạn check-in của ca cứu hộ, không thể hủy.", DispatchErrorCodes.JobAlreadyStarted);
        }
    }

    public WorkerCancelPreviewDto Preview(JobAssignment assignment, DateTime shiftStartUtc, int cancelCountInWindow, int lockCount, int windowDays, bool autoAcceptOn) =>
        new(assignment.AssignmentId, cancelCountInWindow, lockCount, windowDays, false, autoAcceptOn, shiftStartUtc, false, RescueFreeCancel: true);

    public async Task<WorkerCancelResultDto> CancelAsync(
        JobAssignment assignment, int workerId, int cancelCountInWindow, int lockCount, int windowDays, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        if (!await repository.TryCancelRescueAsync(assignment.AssignmentId, workerId, now, cancellationToken))
        {
            throw new BusinessRuleViolationException("Ca này không còn ở trạng thái đã nhận.", DispatchErrorCodes.InvalidAssignmentState);
        }

        await canceller.CancelAsync(assignment.OrderId, Reason, now, cancellationToken);

        // No violation, the lock count is unchanged and the order is cancelled: nothing is re-dispatched.
        return new WorkerCancelResultDto(
            assignment.AssignmentId, DbEnum.ToDb(JobAssignmentStatus.Cancelled), cancelCountInWindow, lockCount, windowDays, false,
            WorkerCancelOutcomes.CancelledRefunded, null);
    }
}
