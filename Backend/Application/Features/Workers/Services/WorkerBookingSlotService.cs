using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Features.Workers.Helpers;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Application.Features.Workers.Services;

public sealed class WorkerBookingSlotService(
    IWorkerRepository workerRepository,
    ICurrentUser currentUser,
    IClock clock) : IWorkerBookingSlotService
{
    /// <summary>409 code when the slot is held by an offer or booked by a job (contracts workers.md 2.3.2).</summary>
    public const string SlotInUseCode = "SLOT_IN_USE";

    public async Task<ApiResponse<IReadOnlyList<BookingSlotResponse>>> GetSlotsAsync(DateOnly? startDate, DateOnly? endDate, CancellationToken ct = default)
    {
        if (currentUser.UserId is not int workerId)
        {
            throw new ForbiddenAccessException("Authenticated worker user ID is required.");
        }

        var start = startDate ?? clock.LocalToday;
        var end = endDate ?? start.AddDays(7);

        if (end < start)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "endDate", ["endDate must be greater than or equal to startDate."] }
            });
        }

        var slots = await workerRepository.GetWorkerSlotsAsync(workerId, start, end, ct);

        var responses = slots.Select(s => new BookingSlotResponse(
            s.SlotId,
            s.WorkerId,
            s.SlotDate,
            s.ShiftCode,
            s.StartTime.ToString("HH:mm"),
            s.EndTime.ToString("HH:mm"),
            s.SlotStatus == "AVAILABLE"
        )).ToList();

        return ApiResponse<IReadOnlyList<BookingSlotResponse>>.Ok(responses, "Retrieved booking slots successfully.");
    }

    public async Task<ApiResponse<BookingSlotResponse>> ToggleAsync(ToggleBookingSlotRequest request, CancellationToken ct = default)
    {
        if (currentUser.UserId is not int workerId)
        {
            throw new ForbiddenAccessException("Authenticated worker user ID is required.");
        }

        // The slot date is a Vietnam local date (shift times are local, decisions G-3), so "today" is the local date, not the UTC one.
        if (request.SlotDate < clock.LocalToday)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "slotDate", ["Cannot toggle availability slots in the past."] }
            });
        }

        var nowUtc = clock.UtcNow;
        var shiftCode = ShiftHelper.NormalizeShiftCode(request.ShiftCode);
        var (startTime, endTime) = ShiftHelper.GetShiftTimes(shiftCode);
        string newStatus = request.IsActive ? "AVAILABLE" : "BLOCKED";

        var existingSlot = await workerRepository.GetWorkerSlotByShiftAsync(workerId, request.SlotDate, shiftCode, ct);

        BookingSlot targetSlot;

        if (existingSlot != null)
        {
            EnsureToggleable(existingSlot);
            existingSlot.SlotStatus = newStatus;
            existingSlot.UpdatedAt = nowUtc;
            workerRepository.UpdateSlot(existingSlot);
            await workerRepository.SaveChangesAsync(ct);
            targetSlot = existingSlot;
        }
        else
        {
            targetSlot = new BookingSlot
            {
                WorkerId = workerId,
                SlotDate = request.SlotDate,
                ShiftCode = shiftCode,
                StartTime = startTime,
                EndTime = endTime,
                SlotSource = "FREELANCER",
                SlotStatus = newStatus,
                UpdatedAt = nowUtc
            };

            try
            {
                await workerRepository.AddSlotAsync(targetSlot, ct);
                await workerRepository.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Unique constraint UNIQUE(worker_id, slot_date, shift_code) conflict during concurrent insertion.
                // Fallback gracefully to query and update existing row idempotently.
                var concurrentSlot = await workerRepository.GetWorkerSlotByShiftAsync(workerId, request.SlotDate, shiftCode, ct);
                if (concurrentSlot == null)
                {
                    // Not that race: no row exists, so the insert failed for another reason (foreign key, truncation, timeout). The slot was
                    // never saved, so do not answer success for it.
                    throw;
                }

                EnsureToggleable(concurrentSlot);
                concurrentSlot.SlotStatus = newStatus;
                concurrentSlot.UpdatedAt = nowUtc;
                workerRepository.UpdateSlot(concurrentSlot);
                await workerRepository.SaveChangesAsync(ct);
                targetSlot = concurrentSlot;
            }
        }

        var response = new BookingSlotResponse(
            targetSlot.SlotId,
            targetSlot.WorkerId,
            targetSlot.SlotDate,
            targetSlot.ShiftCode,
            targetSlot.StartTime.ToString("HH:mm"),
            targetSlot.EndTime.ToString("HH:mm"),
            targetSlot.SlotStatus == "AVAILABLE"
        );

        string action = request.IsActive ? "enabled" : "disabled";
        return ApiResponse<BookingSlotResponse>.Ok(response, $"Slot {targetSlot.ShiftCode} on {targetSlot.SlotDate:yyyy-MM-dd} {action} successfully.");
    }

    /// <summary>
    /// The worker's toggle only moves AVAILABLE &lt;-&gt; BLOCKED. A HELD or BOOKED slot belongs to Dispatch: switching it off would leave a
    /// job on a blocked shift, switching it on would let the same shift be offered to a second order.
    /// </summary>
    private static void EnsureToggleable(BookingSlot slot)
    {
        if (slot.SlotStatus is not ("AVAILABLE" or "BLOCKED"))
        {
            throw new BusinessRuleViolationException("Ca này đang có đơn (đang được mời hoặc đã nhận), không thể bật/tắt.", SlotInUseCode);
        }
    }
}
