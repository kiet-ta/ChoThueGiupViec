using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Booking;

/// <summary>The business 409 codes of POST /api/v1/booking/orders (contract booking.md 3.3). Clients branch on the code, never on the message.</summary>
public static class BookingErrorCodes
{
    public const string ShiftInPast = "SHIFT_IN_PAST";
    public const string PremiumLeadTime = "PREMIUM_LEAD_TIME";
    public const string FullyBooked = "FULLY_BOOKED";

    /// <summary>Decisions D34: a timed Economy job starts sooner than <c>Booking.EconomyMinLeadMinutes</c> from now.</summary>
    public const string LeadTimeTooShort = "LEAD_TIME_TOO_SHORT";
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

    /// <summary>Start of the job in UTC: the order's own start time (decisions D34) or the shift start; null for an unknown shift.</summary>
    public static DateTime? GetJobStartUtc(JobOrder order, IClock clock) => JobToUtc(order, clock, end: false);

    /// <summary>End of the job in UTC: the order's own end time (decisions D34) or the shift end; null for an unknown shift.</summary>
    public static DateTime? GetJobEndUtc(JobOrder order, IClock clock) => JobToUtc(order, clock, end: true);

    private static DateTime? JobToUtc(JobOrder order, IClock clock, bool end)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(clock);
        if (JobWindow.OfOrder(order.ShiftCode, order.StartTime, order.EndTime) is not { } window)
        {
            return null;
        }

        var local = DateTime.SpecifyKind(order.ScheduledDate.ToDateTime(end ? window.End : window.Start), DateTimeKind.Unspecified);
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
