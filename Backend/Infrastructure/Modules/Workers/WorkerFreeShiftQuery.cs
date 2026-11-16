using System.Globalization;
using CommonService.Application.Common.Options;
using CommonService.Application.Features.Booking;
using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.Ports;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CommonService.Infrastructure.Modules.Workers;

/// <summary>
/// Real <see cref="IWorkerFreeShiftQuery"/>: the worker's <c>AVAILABLE</c> BOOKING_SLOT rows in a date range, with the times their live jobs take
/// that day (decisions D34: a timed job keeps the slot open, so the slot alone cannot tell a customer the worker is busy, BOOK-BUSY-01).
/// </summary>
public class WorkerFreeShiftQuery(AppDbContext dbContext, IOptions<BusinessRules>? rules = null) : IWorkerFreeShiftQuery
{
    private readonly BusinessRules _rules = rules?.Value ?? new BusinessRules();

    public async Task<IReadOnlyList<WorkerFreeShift>> GetFreeShiftsAsync(
        int workerId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.BookingSlots.AsNoTracking()
            .Where(s => s.WorkerId == workerId
                && s.SlotDate >= from
                && s.SlotDate <= to
                && s.SlotStatus == DispatchSlotStatuses.Available)
            .OrderBy(s => s.SlotDate)
            .ThenBy(s => s.StartTime)
            .Select(s => new { s.SlotDate, s.ShiftCode })
            .ToListAsync(cancellationToken);

        // One read per day that has a free shift (at most Booking.MaxDaysAhead + 1).
        var perDay = new Dictionary<DateOnly, (IReadOnlyList<BusyWindow> Busy, decimal Remaining)>();
        foreach (var date in rows.Select(r => r.SlotDate).Distinct())
        {
            var jobs = (await WorkerJobTimes.LoadAsync(dbContext, [workerId], date, cancellationToken)).GetValueOrDefault(workerId) ?? [];
            var busy = jobs.OrderBy(j => j.Start).Select(j => Widen(j, _rules.Booking.TravelBufferMinutes)).ToList();
            var remaining = Math.Max(0m, _rules.Worker.MaxJobHoursPerDay - jobs.Sum(JobWindow.Hours));
            perDay[date] = (busy, remaining);
        }

        return rows.Select(r => new WorkerFreeShift(r.SlotDate, r.ShiftCode, perDay[r.SlotDate].Busy, perDay[r.SlotDate].Remaining)).ToList();
    }

    public async Task<bool> IsFreeForJobAsync(
        int workerId, DateOnly date, string shiftCode, TimeOnly? startTime, TimeOnly? endTime, CancellationToken cancellationToken = default)
    {
        var slotOpen = await dbContext.BookingSlots.AsNoTracking()
            .AnyAsync(s => s.WorkerId == workerId && s.SlotDate == date && s.ShiftCode == shiftCode
                && s.SlotStatus == DispatchSlotStatuses.Available, cancellationToken);
        if (!slotOpen || JobWindow.OfOrder(shiftCode, startTime, endTime) is not { } window)
        {
            return false;
        }

        var jobs = (await WorkerJobTimes.LoadAsync(dbContext, [workerId], date, cancellationToken)).GetValueOrDefault(workerId);
        return WorkerJobTimes.Fits(jobs, window, _rules);
    }

    /// <summary>The job window widened by the travel buffer on each side, kept inside the day.</summary>
    private static BusyWindow Widen((TimeOnly Start, TimeOnly End) job, int bufferMinutes)
    {
        var buffer = TimeSpan.FromMinutes(Math.Max(0, bufferMinutes));
        var start = job.Start.ToTimeSpan() - buffer;
        var end = job.End.ToTimeSpan() + buffer;
        var dayEnd = TimeSpan.FromHours(24) - TimeSpan.FromMinutes(1);
        return new BusyWindow(Hhmm(start < TimeSpan.Zero ? TimeSpan.Zero : start), Hhmm(end > dayEnd ? dayEnd : end));
    }

    private static string Hhmm(TimeSpan time) => TimeOnly.FromTimeSpan(time).ToString("HH:mm", CultureInfo.InvariantCulture);
}
