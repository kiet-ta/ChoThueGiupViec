namespace CommonService.Application.Interfaces.Ports;

/// <summary>A time (local, HH:mm) range in which a worker cannot start another job: one live job widened by the travel buffer (decisions D34, BOOK-BUSY-01).</summary>
public record BusyWindow(string StartTime, string EndTime);

/// <summary>
/// One shift a worker has free: an <c>AVAILABLE</c> BOOKING_SLOT row. A timed job keeps its slot open (D34), so the shift alone does not say the
/// worker is free all through it: <paramref name="BusyWindows"/> are the times of that day the worker's live jobs take (each already widened by
/// <c>Booking.TravelBufferMinutes</c>) and <paramref name="RemainingHours"/> is what is left of <c>Worker.MaxJobHoursPerDay</c> that day.
/// Null = not known (the older shape).
/// </summary>
public record WorkerFreeShift(
    DateOnly Date,
    string ShiftCode,
    IReadOnlyList<BusyWindow>? BusyWindows = null,
    decimal? RemainingHours = null);

/// <summary>
/// The free shifts of one worker, so a customer can see when a favorite worker can come (customers.md 2.3). Only <c>AVAILABLE</c>
/// slots count: blocked by the worker, held by an offer or booked are all "not free". Implemented by Workers (M4).
/// </summary>
public interface IWorkerFreeShiftQuery
{
    /// <summary>Free shifts from <paramref name="from"/> to <paramref name="to"/> (both included), ordered by date then shift, with the busy times of each day.</summary>
    Task<IReadOnlyList<WorkerFreeShift>> GetFreeShiftsAsync(int workerId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the worker can take a job in this shift now: the slot is <c>AVAILABLE</c>, no live job of theirs overlaps the window (with the
    /// travel buffer) and the day stays within the daily hours (decisions D34 item 4). A null time is the whole shift.
    /// </summary>
    Task<bool> IsFreeForJobAsync(
        int workerId, DateOnly date, string shiftCode, TimeOnly? startTime, TimeOnly? endTime, CancellationToken cancellationToken = default);
}
