using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Features.Workers.Helpers;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Application.Features.Workers.Commands;

public sealed record ToggleBookingSlotCommand(ToggleBookingSlotRequest Request) : IRequest<ApiResponse<BookingSlotResponse>>;

public sealed class ToggleBookingSlotCommandHandler(
    IWorkerRepository workerRepository,
    ICurrentUser currentUser)
    : IRequestHandler<ToggleBookingSlotCommand, ApiResponse<BookingSlotResponse>>
{
    public async Task<ApiResponse<BookingSlotResponse>> Handle(ToggleBookingSlotCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not int workerId)
        {
            throw new ForbiddenAccessException("Authenticated worker user ID is required.");
        }

        var req = command.Request;
        if (req.SlotDate < DateOnly.FromDateTime(DateTime.UtcNow.Date))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "slotDate", ["Cannot toggle availability slots in the past."] }
            });
        }

        var shiftCode = ShiftHelper.NormalizeShiftCode(req.ShiftCode);
        var (startTime, endTime) = ShiftHelper.GetShiftTimes(shiftCode);
        string newStatus = req.IsActive ? "AVAILABLE" : "BLOCKED";

        var existingSlot = await workerRepository.GetWorkerSlotByShiftAsync(workerId, req.SlotDate, shiftCode, cancellationToken);

        BookingSlot targetSlot;

        if (existingSlot != null)
        {
            existingSlot.SlotStatus = newStatus;
            existingSlot.UpdatedAt = DateTime.UtcNow;
            workerRepository.UpdateSlot(existingSlot);
            await workerRepository.SaveChangesAsync(cancellationToken);
            targetSlot = existingSlot;
        }
        else
        {
            targetSlot = new BookingSlot
            {
                WorkerId = workerId,
                SlotDate = req.SlotDate,
                ShiftCode = shiftCode,
                StartTime = startTime,
                EndTime = endTime,
                SlotSource = "FREELANCER",
                SlotStatus = newStatus,
                UpdatedAt = DateTime.UtcNow
            };

            try
            {
                await workerRepository.AddSlotAsync(targetSlot, cancellationToken);
                await workerRepository.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Unique constraint UNIQUE(worker_id, slot_date, shift_code) conflict during concurrent insertion.
                // Fallback gracefully to query and update existing row idempotently.
                var concurrentSlot = await workerRepository.GetWorkerSlotByShiftAsync(workerId, req.SlotDate, shiftCode, cancellationToken);
                if (concurrentSlot != null)
                {
                    concurrentSlot.SlotStatus = newStatus;
                    concurrentSlot.UpdatedAt = DateTime.UtcNow;
                    workerRepository.UpdateSlot(concurrentSlot);
                    await workerRepository.SaveChangesAsync(cancellationToken);
                    targetSlot = concurrentSlot;
                }
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

        string action = req.IsActive ? "enabled" : "disabled";
        return ApiResponse<BookingSlotResponse>.Ok(response, $"Slot {targetSlot.ShiftCode} on {targetSlot.SlotDate:yyyy-MM-dd} {action} successfully.");
    }
}
