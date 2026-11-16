using CommonService.Application.Interfaces.Ports;

namespace CommonService.Application.Features.Customers.Dtos;

/// <summary>
/// One shift a favorite worker is free for (contract customers.md §2.3): the booking form greys out every other shift, and, inside this one,
/// the times that overlap <see cref="BusyWindows"/> (BOOK-BUSY-01, decisions D41: a timed job keeps the shift open).
/// </summary>
public class FavoriteWorkerFreeShiftDto
{
    public DateOnly Date { get; set; }

    /// <summary>SHIFT_MORNING, SHIFT_AFTERNOON or SHIFT_EVENING.</summary>
    public string ShiftCode { get; set; } = string.Empty;

    /// <summary>The times of that day the worker's live jobs take, each already widened by the travel buffer. A new job must not overlap them.</summary>
    public IReadOnlyList<BusyWindow> BusyWindows { get; set; } = [];

    /// <summary>The hours the worker can still take that day (daily cap minus the live jobs); null when unknown.</summary>
    public decimal? RemainingHours { get; set; }
}
