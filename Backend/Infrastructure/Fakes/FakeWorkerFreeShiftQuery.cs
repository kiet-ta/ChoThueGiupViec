using System.Collections.Concurrent;
using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Fakes;

/// <summary>In-memory free shifts for tests and for running Customers before the Workers implementation is wired. Empty by default.</summary>
public sealed class FakeWorkerFreeShiftQuery : IWorkerFreeShiftQuery
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
}
