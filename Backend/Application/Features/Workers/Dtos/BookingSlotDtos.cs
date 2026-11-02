namespace CommonService.Application.Features.Workers.Dtos;

public record ToggleBookingSlotRequest(
    DateOnly SlotDate,
    string ShiftCode,
    bool IsActive
);

public record BookingSlotResponse(
    int SlotId,
    int WorkerId,
    DateOnly SlotDate,
    string ShiftCode,
    string StartTime,
    string EndTime,
    bool IsActive
);
