using CommonService.Application.Common.Models;
using CommonService.Application.Features.Workers.Dtos;

namespace CommonService.Application.Features.Workers.Services;

/// <summary>Availability slots of the signed-in worker (contract workers.md §2.3). The worker is read from ICurrentUser.</summary>
public interface IWorkerBookingSlotService
{
    /// <summary>Slots in the date range; start defaults to today and end to start + 7 days (contract §2.3.1).</summary>
    Task<ApiResponse<IReadOnlyList<BookingSlotResponse>>> GetSlotsAsync(DateOnly? startDate, DateOnly? endDate, CancellationToken ct = default);

    /// <summary>Moves a slot AVAILABLE &lt;-&gt; BLOCKED; a slot held by an offer or booked by a job is refused with <c>SLOT_IN_USE</c> (contract §2.3.2).</summary>
    Task<ApiResponse<BookingSlotResponse>> ToggleAsync(ToggleBookingSlotRequest request, CancellationToken ct = default);
}
