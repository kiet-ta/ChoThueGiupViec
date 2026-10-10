namespace CommonService.Application.Interfaces.Ports;

/// <param name="Date">Shift date.</param>
/// <param name="ShiftCode">Shift code as stored in BOOKING_SLOT.shift_code.</param>
/// <param name="RequiredWorkers">1, or 2 when the area is over 80 m2 (decisions Q01).</param>
/// <param name="OrderId">The order the hold is for (contract booking.md B8): the reservation is keyed by it, Booking stores no reservation id on JOB_ORDER.</param>
public sealed record CapacityRequest(DateOnly Date, string ShiftCode, int RequiredWorkers, long OrderId);

/// <summary>An atomic hold on agency slots (Premium phase 1). Release it if the order is not paid in time.</summary>
public sealed record CapacityReservation(Guid ReservationId, int AgencyId, IReadOnlyList<int> SlotIds);

/// <summary>Premium capacity check and atomic slot locking. Implemented by Agencies (M5); consumed by Booking (M2) and Dispatch (M3).</summary>
public interface IAgencyCapacityService
{
    /// <summary>True when some agency can staff the request right now (read only).</summary>
    Task<bool> HasCapacityAsync(CapacityRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lock slots atomically. Returns null when there is not enough capacity ("fully booked", decisions Q13).</summary>
    Task<CapacityReservation?> TryReserveAsync(CapacityRequest request, CancellationToken cancellationToken = default);

    Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Release every hold made for the order (<see cref="CapacityRequest.OrderId"/>): called when a Premium order is cancelled or its
    /// QR expires (contracts booking.md 3.3 rule 6, payments.md 3.3). Idempotent: an order without a hold is a no-op.
    /// </summary>
    Task ReleaseByOrderAsync(long orderId, CancellationToken cancellationToken = default);
}
