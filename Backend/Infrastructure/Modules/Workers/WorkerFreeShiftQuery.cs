using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.Ports;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Workers;

/// <summary>Real <see cref="IWorkerFreeShiftQuery"/>: the worker's <c>AVAILABLE</c> BOOKING_SLOT rows in a date range.</summary>
public sealed class WorkerFreeShiftQuery(AppDbContext dbContext) : IWorkerFreeShiftQuery
{
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

        return rows.Select(r => new WorkerFreeShift(r.SlotDate, r.ShiftCode)).ToList();
    }
}
