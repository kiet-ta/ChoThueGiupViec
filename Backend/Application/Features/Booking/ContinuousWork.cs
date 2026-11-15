namespace CommonService.Application.Features.Booking;

/// <summary>
/// Decision D35: an ECONOMY order of a large house (area over <c>Area.StandardMaxM2</c>) is done by ONE worker, who may need a
/// 2nd consecutive job (the next shift of the same day). <c>JOB_ORDER.continuous_work</c> marks it; older large ECONOMY orders keep
/// <c>required_workers = 2</c> (BR-02) and <c>continuous_work = false</c>.
/// </summary>
public static class ContinuousWork
{
    /// <summary>The shift that follows <paramref name="shiftCode"/> on the same day (the 2nd job of a continuous order); null after the evening shift or for an unknown code.</summary>
    public static string? NextShift(string? shiftCode) => shiftCode switch
    {
        BookingShifts.Morning => BookingShifts.Afternoon,
        BookingShifts.Afternoon => BookingShifts.Evening,
        _ => null,
    };
}
