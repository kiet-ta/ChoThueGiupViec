using CommonService.Application.Common.Options;

namespace CommonService.Application.Features.Booking;

/// <summary>
/// The local time window of a job (decisions D34). An order may carry its own <c>start_time</c> / <c>end_time</c> inside the shift; without
/// them the job is the whole shift (older orders, Premium). Pure: no DB, I/O or clock. Local time is Asia/Ho_Chi_Minh (G-3).
/// </summary>
public static class JobWindow
{
    /// <summary>Start times are on this step (30 minutes).</summary>
    public const int StartStepMinutes = 30;

    /// <summary>Durations are on this step (0.5 h, as the extension hours of B11).</summary>
    public const decimal DurationStepHours = 0.5m;

    /// <summary>The order's window: its own times when it has both, otherwise the shift's; null for an unknown shift code.</summary>
    public static (TimeOnly Start, TimeOnly End)? OfOrder(string? shiftCode, TimeOnly? startTime, TimeOnly? endTime) =>
        startTime is { } start && endTime is { } end ? (start, end) : BookingShifts.GetLocalTimes(shiftCode);

    /// <summary>
    /// The window of one assignment. It is the order's window when the assignment's slot is the order's shift; a slot of another shift (the
    /// BR-02 consecutive-shift fallback, D7) is worked for the whole of that shift.
    /// </summary>
    public static (TimeOnly Start, TimeOnly End) OfAssignment(
        string orderShiftCode, TimeOnly? orderStart, TimeOnly? orderEnd, string slotShiftCode, TimeOnly slotStart, TimeOnly slotEnd) =>
        orderStart is { } start && orderEnd is { } end && string.Equals(orderShiftCode, slotShiftCode, StringComparison.Ordinal)
            ? (start, end)
            : (slotStart, slotEnd);

    /// <summary>True when the two windows, each widened by <paramref name="bufferMinutes"/> on the side facing the other, overlap.</summary>
    public static bool Overlaps((TimeOnly Start, TimeOnly End) a, (TimeOnly Start, TimeOnly End) b, int bufferMinutes)
    {
        var buffer = TimeSpan.FromMinutes(Math.Max(0, bufferMinutes));
        return a.Start.ToTimeSpan() < b.End.ToTimeSpan() + buffer && b.Start.ToTimeSpan() < a.End.ToTimeSpan() + buffer;
    }

    /// <summary>Length of a window in hours.</summary>
    public static decimal Hours((TimeOnly Start, TimeOnly End) window) =>
        (decimal)(window.End.ToTimeSpan() - window.Start.ToTimeSpan()).TotalMinutes / 60m;

    /// <summary>
    /// Suggested duration for one worker: area per worker / <c>Booking.M2PerHour</c>, rounded up to 0.5 h and kept in
    /// [<c>Booking.MinDurationHours</c>, <c>Shift.MaxHours</c>].
    /// </summary>
    public static decimal SuggestedHours(decimal totalAreaM2, int requiredWorkers, BusinessRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var perWorker = totalAreaM2 / Math.Max(1, requiredWorkers);
        var raw = rules.Booking.M2PerHour > 0 ? perWorker / rules.Booking.M2PerHour : rules.Shift.MaxHours;
        var rounded = Math.Ceiling(raw / DurationStepHours) * DurationStepHours;
        return Math.Clamp(rounded, rules.Booking.MinDurationHours, rules.Shift.MaxHours);
    }

    /// <summary>
    /// Checks a requested start + duration against the shift (decisions D34): both given or neither, start on a 30-minute step, duration in
    /// [<c>Booking.MinDurationHours</c>, <c>Shift.MaxHours</c>] on a 0.5 h step, the job inside the shift. Returns the field errors (empty
    /// when valid) and the window (null when no time was asked or an error was found).
    /// </summary>
    public static (Dictionary<string, string[]> Errors, (TimeOnly Start, TimeOnly End)? Window) Validate(
        string? shiftCode, TimeOnly? startTime, decimal? durationHours, BusinessRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (startTime is null && durationHours is null)
        {
            return (errors, null);
        }

        if (startTime is null || durationHours is null)
        {
            errors[startTime is null ? "startTime" : "durationHours"] = ["startTime and durationHours must be sent together."];
            return (errors, null);
        }

        var min = rules.Booking.MinDurationHours;
        var max = (decimal)rules.Shift.MaxHours;
        if (durationHours < min || durationHours > max || durationHours.Value % DurationStepHours != 0)
        {
            errors["durationHours"] = [$"durationHours must be from {min} to {max} in steps of {DurationStepHours}."];
        }

        var start = startTime.Value;
        if (start.Second != 0 || start.Minute % StartStepMinutes != 0)
        {
            errors["startTime"] = [$"startTime must be on a {StartStepMinutes}-minute step (HH:00 or HH:30)."];
        }

        if (errors.Count > 0 || BookingShifts.GetLocalTimes(shiftCode) is not { } shift)
        {
            return (errors, null);
        }

        var endSpan = start.ToTimeSpan() + TimeSpan.FromMinutes((double)(durationHours.Value * 60m));
        if (start < shift.Start || endSpan > shift.End.ToTimeSpan())
        {
            errors["startTime"] = [$"The job must fit inside the shift ({shift.Start:HH\\:mm}-{shift.End:HH\\:mm})."];
            return (errors, null);
        }

        return (errors, (start, TimeOnly.FromTimeSpan(endSpan)));
    }
}
