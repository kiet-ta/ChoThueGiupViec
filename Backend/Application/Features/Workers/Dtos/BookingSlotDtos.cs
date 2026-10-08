namespace CommonService.Application.Features.Workers.Dtos;

public sealed record ToggleBookingSlotRequest(
    DateOnly SlotDate,
    string ShiftCode,
    bool IsActive
);

public sealed record BookingSlotResponse(
    int SlotId,
    int WorkerId,
    DateOnly SlotDate,
    string ShiftCode,
    string StartTime,
    string EndTime,
    bool IsActive
);
