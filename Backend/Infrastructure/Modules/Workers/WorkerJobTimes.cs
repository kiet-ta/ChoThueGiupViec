using CommonService.Application.Common.Options;
using CommonService.Application.Features.Booking;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Workers;

/// <summary>
/// The time a worker's live jobs take on one day (decisions D34). A shift slot stays open after a timed job, so whether a worker is free
/// for a new job is decided here: no live job may overlap the new one (each widened by <c>Booking.TravelBufferMinutes</c>) and the day's
/// jobs together stay within <c>Worker.MaxJobHoursPerDay</c>. Used by the candidate query and, again, inside the offer transaction.
/// </summary>
public static class WorkerJobTimes
{
    /// <summary>The live windows (local time) of each worker's jobs on <paramref name="date"/>; a cancelled or reassigned job takes no time.</summary>
    public static async Task<Dictionary<int, List<(TimeOnly Start, TimeOnly End)>>> LoadAsync(
        AppDbContext db, IReadOnlyCollection<int> workerIds, DateOnly date, CancellationToken cancellationToken, long? excludeOrderId = null)
    {
        var result = new Dictionary<int, List<(TimeOnly Start, TimeOnly End)>>();
        if (workerIds.Count == 0)
        {
            return result;
        }

        var rows = await (
            from assignment in db.JobAssignments.AsNoTracking()
            join slot in db.BookingSlots.AsNoTracking() on assignment.SlotId equals slot.SlotId
            join order in db.JobOrders.AsNoTracking() on assignment.OrderId equals order.OrderId
            where workerIds.Contains(assignment.WorkerId)
               && slot.SlotDate == date
               && (excludeOrderId == null || assignment.OrderId != excludeOrderId)
               && assignment.AssignmentStatus != JobAssignmentStatus.Cancelled
               && assignment.AssignmentStatus != JobAssignmentStatus.CancelledByWorker
               && assignment.AssignmentStatus != JobAssignmentStatus.Reassigned
            select new
            {
                assignment.WorkerId,
                OrderShift = order.ShiftCode,
                OrderStart = order.StartTime,
                OrderEnd = order.EndTime,
                SlotShift = slot.ShiftCode,
                SlotStart = slot.StartTime,
                SlotEnd = slot.EndTime,
            }).ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            if (!result.TryGetValue(row.WorkerId, out var list))
            {
                result[row.WorkerId] = list = [];
            }

            list.Add(JobWindow.OfAssignment(row.OrderShift, row.OrderStart, row.OrderEnd, row.SlotShift, row.SlotStart, row.SlotEnd));
        }

        return result;
    }

    /// <summary>True when <paramref name="candidate"/> overlaps none of <paramref name="jobs"/> (with the travel buffer) and the day stays within the cap.</summary>
    public static bool Fits(IReadOnlyCollection<(TimeOnly Start, TimeOnly End)>? jobs, (TimeOnly Start, TimeOnly End) candidate, BusinessRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (jobs is null || jobs.Count == 0)
        {
            return JobWindow.Hours(candidate) <= rules.Worker.MaxJobHoursPerDay;
        }

        if (jobs.Any(job => JobWindow.Overlaps(job, candidate, rules.Booking.TravelBufferMinutes)))
        {
            return false;
        }

        return jobs.Sum(JobWindow.Hours) + JobWindow.Hours(candidate) <= rules.Worker.MaxJobHoursPerDay;
    }

    /// <summary>The window an order asks of a slot: its own times inside its shift, the whole slot otherwise (BR-02 next-shift fallback).</summary>
    public static async Task<(TimeOnly Start, TimeOnly End)?> OrderWindowAsync(
        AppDbContext db, long orderId, string slotShiftCode, TimeOnly slotStart, TimeOnly slotEnd, CancellationToken cancellationToken)
    {
        var order = await db.JobOrders.AsNoTracking()
            .Where(o => o.OrderId == orderId)
            .Select(o => new { o.ShiftCode, o.StartTime, o.EndTime })
            .FirstOrDefaultAsync(cancellationToken);
        return order is null
            ? null
            : JobWindow.OfAssignment(order.ShiftCode, order.StartTime, order.EndTime, slotShiftCode, slotStart, slotEnd);
    }

    /// <summary>True when the order is a timed job in that slot's shift (decisions D34): the slot then stays AVAILABLE.</summary>
    public static async Task<bool> IsTimedInShiftAsync(AppDbContext db, long orderId, string slotShiftCode, CancellationToken cancellationToken) =>
        await db.JobOrders.AsNoTracking()
            .AnyAsync(o => o.OrderId == orderId && o.StartTime != null && o.EndTime != null && o.ShiftCode == slotShiftCode, cancellationToken);
}
