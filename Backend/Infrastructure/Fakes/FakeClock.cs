using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Fakes;

/// <summary>Follows the system clock until a test pins it with <see cref="Set"/>. Vietnam has no daylight saving: local time is UTC+7.</summary>
public sealed class FakeClock : IClock
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private DateTime? _pinned;

    public DateTime UtcNow => _pinned ?? DateTime.UtcNow;

    public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(UtcNow));

    public DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).Add(VietnamOffset).ToUnspecified();

    public DateTime ToUtc(DateTime local) => DateTime.SpecifyKind(local.Subtract(VietnamOffset), DateTimeKind.Utc);

    /// <summary>Pin the clock to a UTC instant.</summary>
    public void Set(DateTime utc) => _pinned = DateTime.SpecifyKind(utc, DateTimeKind.Utc);

    /// <summary>Move a pinned clock forward (pins it at the current time first when it was not pinned).</summary>
    public void Advance(TimeSpan by) => _pinned = UtcNow.Add(by);
}

internal static class DateTimeExtensions
{
    public static DateTime ToUnspecified(this DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
}
