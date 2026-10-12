using CommonService.Application.Interfaces.Ports;

namespace CommonService.Tests.Booking;

/// <summary>Records which orders had their Premium hold released; <see cref="ThrowOnRelease"/> makes the release fail.</summary>
internal sealed class RecordingCapacity : IAgencyCapacityService
{
    public List<long> ReleasedOrders { get; } = [];
    public bool ThrowOnRelease { get; set; }

    public Task ReleaseByOrderAsync(long orderId, CancellationToken cancellationToken = default)
    {
        ReleasedOrders.Add(orderId);
        return ThrowOnRelease ? throw new InvalidOperationException("agencies module down") : Task.CompletedTask;
    }

    public Task<bool> HasCapacityAsync(CapacityRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<CapacityReservation?> TryReserveAsync(CapacityRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
