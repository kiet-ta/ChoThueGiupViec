using System.Collections.Concurrent;
using CommonService.Application.Features.Booking;
using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Fakes;

/// <summary>In-memory free shifts for tests and for running Customers before the Workers implementation is wired. Empty by default.</summary>
public class FakeWorkerFreeShiftQuery : IWorkerFreeShiftQuery
{
    private readonly ConcurrentDictionary<int, ConcurrentBag<WorkerFreeShift>> _shifts = new();

    public void Add(int workerId, WorkerFreeShift shift) => _shifts.GetOrAdd(workerId, _ => new ConcurrentBag<WorkerFreeShift>()).Add(shift);

    public Task<IReadOnlyList<WorkerFreeShift>> GetFreeShiftsAsync(
        int workerId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<WorkerFreeShift> free = _shifts.TryGetValue(workerId, out var bag)
            ? bag.Where(s => s.Date >= from && s.Date <= to).OrderBy(s => s.Date).ThenBy(s => s.ShiftCode, StringComparer.Ordinal).ToList()
            : [];
        return Task.FromResult(free);
    }

    /// <summary>Free when the shift was added and none of its busy windows overlaps the job (the windows already include the buffer).</summary>
    public Task<bool> IsFreeForJobAsync(
        int workerId, DateOnly date, string shiftCode, TimeOnly? startTime, TimeOnly? endTime, CancellationToken cancellationToken = default)
    {
        var shift = _shifts.TryGetValue(workerId, out var bag)
            ? bag.FirstOrDefault(s => s.Date == date && s.ShiftCode == shiftCode)
            : null;
        if (shift is null)
        {
            return Task.FromResult(false);
        }

        var window = JobWindow.OfOrder(shiftCode, startTime, endTime);
        var busy = window is { } job && (shift.BusyWindows ?? []).Any(b =>
            job.Start < TimeOnly.ParseExact(b.EndTime, "HH:mm") && TimeOnly.ParseExact(b.StartTime, "HH:mm") < job.End);
        return Task.FromResult(!busy);
    }
}
