using System.Collections.Concurrent;
using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Fakes;

/// <summary>One fake agency (id 1) with a settable number of free slots; holds are atomic.</summary>
public sealed class FakeAgencyCapacityService : IAgencyCapacityService
{
    private const int FakeAgencyId = 1;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<Guid, int> _holds = new();
    private int _remainingSlots = 100;
    private int _nextSlotId = 1;

    public int RemainingSlots
    {
        get { lock (_gate) { return _remainingSlots; } }
        set { lock (_gate) { _remainingSlots = value; } }
    }

    public Task<bool> HasCapacityAsync(CapacityRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(RemainingSlots >= request.RequiredWorkers);

    public Task<CapacityReservation?> TryReserveAsync(CapacityRequest request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_remainingSlots < request.RequiredWorkers)
            {
                return Task.FromResult<CapacityReservation?>(null);
            }

            _remainingSlots -= request.RequiredWorkers;
            var slotIds = Enumerable.Range(0, request.RequiredWorkers).Select(_ => _nextSlotId++).ToArray();
            var id = Guid.NewGuid();
            _holds[id] = request.RequiredWorkers;
            return Task.FromResult<CapacityReservation?>(new CapacityReservation(id, FakeAgencyId, slotIds));
        }
    }

    public Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_holds.TryRemove(reservationId, out var count))
            {
                _remainingSlots += count;
            }
        }

        return Task.CompletedTask;
    }
}
