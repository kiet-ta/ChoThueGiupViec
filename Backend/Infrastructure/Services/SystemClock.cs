using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Services;

/// <summary>
/// Real clock implementation (decisions G-3).
/// Converts UTC to/from Asia/Ho_Chi_Minh (Indochina Time, UTC+7).
/// Supports both Windows ("SE Asia Standard Time") and IANA ("Asia/Ho_Chi_Minh") timezone identifiers.
/// </summary>
public sealed class SystemClock : IClock
{
    private static readonly TimeZoneInfo LocalTimeZone = ResolveVietnamTimeZone();

    public DateTime UtcNow => DateTime.UtcNow;

    public DateTime ToLocal(DateTime utc)
    {
        var utcSpecified = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utcSpecified, LocalTimeZone);
    }

    public DateTime ToUtc(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, LocalTimeZone);
    }

    public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(UtcNow));

    private static TimeZoneInfo ResolveVietnamTimeZone()
    {
        // Try Windows ID first, then IANA ID, fallback to fixed +7 offset
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.CreateCustomTimeZone(
                    "Asia/Ho_Chi_Minh_Custom",
                    TimeSpan.FromHours(7),
                    "Asia/Ho_Chi_Minh (UTC+7)",
                    "Asia/Ho_Chi_Minh (UTC+7)");
            }
        }
    }
}
