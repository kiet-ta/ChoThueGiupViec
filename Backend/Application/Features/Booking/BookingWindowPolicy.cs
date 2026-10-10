using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Booking;

/// <summary>The business 409 codes of POST /api/booking/orders (contract booking.md 3.3). Clients branch on the code, never on the message.</summary>
public static class BookingErrorCodes
{
    public const string ShiftInPast = "SHIFT_IN_PAST";
    public const string PremiumLeadTime = "PREMIUM_LEAD_TIME";
    public const string FullyBooked = "FULLY_BOOKED";
}

/// <summary>
/// Contract booking.md 3.3 rules 3 and 4: the shift start must be in the future; a PREMIUM shift must start at least
/// <c>Premium.MinLeadHours</c> from now (Q13). Pure: the clock is the only input, no DB.
/// The booking horizon (B3, <c>Booking.MaxDaysAhead</c>) is not applied here: its key is not in BusinessRules yet (Scope exception #247).
/// </summary>
public static class BookingWindowPolicy
{
    /// <summary>Shift start in UTC for a date and a shift code (local time converted by IClock only, G-3), or null for an unknown code.</summary>
    public static DateTime? GetShiftStartUtc(DateOnly scheduledDate, string? shiftCode, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var times = BookingShifts.GetLocalTimes(shiftCode);
        if (times is null)
        {
            return null;
        }

        var local = DateTime.SpecifyKind(scheduledDate.ToDateTime(times.Value.Start), DateTimeKind.Unspecified);
        return clock.ToUtc(local);
    }

    /// <summary>Null when the window is acceptable, otherwise <see cref="BookingErrorCodes.ShiftInPast"/> or <see cref="BookingErrorCodes.PremiumLeadTime"/>.</summary>
    public static string? Check(ServiceTier tier, DateTime shiftStartUtc, DateTime nowUtc, int premiumMinLeadHours)
    {
        if (shiftStartUtc <= nowUtc)
        {
            return BookingErrorCodes.ShiftInPast;
        }

        if (tier == ServiceTier.Premium && shiftStartUtc < nowUtc.AddHours(premiumMinLeadHours))
        {
            return BookingErrorCodes.PremiumLeadTime;
        }

        return null;
    }
}
