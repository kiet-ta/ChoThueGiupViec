namespace CommonService.Application.Interfaces.Ports;

/// <summary>One shift a worker has free: an <c>AVAILABLE</c> BOOKING_SLOT row.</summary>
public sealed record WorkerFreeShift(DateOnly Date, string ShiftCode);

/// <summary>
/// The free shifts of one worker, so a customer can see when a favorite worker can come (customers.md 2.3). Only <c>AVAILABLE</c>
/// slots count: blocked by the worker, held by an offer or booked are all "not free". Implemented by Workers (M4).
/// </summary>
public interface IWorkerFreeShiftQuery
{
    /// <summary>Free shifts from <paramref name="from"/> to <paramref name="to"/> (both included), ordered by date then shift.</summary>
    Task<IReadOnlyList<WorkerFreeShift>> GetFreeShiftsAsync(int workerId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
