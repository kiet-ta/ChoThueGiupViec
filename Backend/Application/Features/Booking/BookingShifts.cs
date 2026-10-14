namespace CommonService.Application.Features.Booking;

/// <summary>Block Slot shift codes and local times (PRD 2.6, contract booking.md 1.2, B1). Local time is Asia/Ho_Chi_Minh (G-3).</summary>
public static class BookingShifts
{
    public const string Morning = "SHIFT_MORNING";
    public const string Afternoon = "SHIFT_AFTERNOON";
    public const string Evening = "SHIFT_EVENING";

    /// <summary>Local start and end of the shift, or null for an unknown code. The code is matched exactly.</summary>
    public static (TimeOnly Start, TimeOnly End)? GetLocalTimes(string? shiftCode) => shiftCode switch
    {
        Morning => (new TimeOnly(8, 0), new TimeOnly(12, 0)),
        Afternoon => (new TimeOnly(13, 0), new TimeOnly(17, 0)),
        Evening => (new TimeOnly(17, 30), new TimeOnly(20, 30)),
        _ => null,
    };
}
