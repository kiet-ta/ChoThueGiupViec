using System.Collections.Concurrent;
using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Fakes;

/// <summary>One fake agency (id 1) with a settable number of free slots; holds are atomic.</summary>
public sealed class FakeAgencyCapacityService : IAgencyCapacityService
{
    private const int FakeAgencyId = 1;

    /// <summary>The fake agency is on the FREE package: commission 0.200 (decisions Q08 seed, Q11).</summary>
    private const decimal FreePackageCommissionRate = 0.200m;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<Guid, int> _holds = new();
    private readonly Dictionary<Guid, long> _orderOfHold = new();
    private readonly Dictionary<Guid, CapacityReservation> _reservations = new();
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
            _orderOfHold[id] = request.OrderId;
            var reservation = new CapacityReservation(id, FakeAgencyId, slotIds, FreePackageCommissionRate);
            _reservations[id] = reservation;
            return Task.FromResult<CapacityReservation?>(reservation);
        }
    }

    public Task<CapacityReservation?> GetReservationByOrderAsync(long orderId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var reservationId = _orderOfHold.Where(hold => hold.Value == orderId).Select(hold => (Guid?)hold.Key).FirstOrDefault();
            return Task.FromResult(reservationId is { } id && _reservations.TryGetValue(id, out var reservation) ? reservation : null);
        }
    }

    public Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ReleaseHold(reservationId);
        }

        return Task.CompletedTask;
    }

    public Task ReleaseByOrderAsync(long orderId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            foreach (var reservationId in _orderOfHold.Where(hold => hold.Value == orderId).Select(hold => hold.Key).ToArray())
            {
                ReleaseHold(reservationId);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>Call inside the gate.</summary>
    private void ReleaseHold(Guid reservationId)
    {
        _orderOfHold.Remove(reservationId);
        _reservations.Remove(reservationId);
        if (_holds.TryRemove(reservationId, out var count))
        {
            _remainingSlots += count;
        }
    }
}
