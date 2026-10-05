namespace CommonService.Application.Interfaces.Ports;

/// <summary>
/// The only place that reads the time and converts UTC to/from Asia/Ho_Chi_Minh (decisions G-3).
/// Handlers never call DateTime.UtcNow, so tests control time (offer timeout 30 s, OTP expiry, ...).
/// </summary>
public interface IClock
{
    /// <summary>Current instant in UTC (stored timestamps are UTC, DATETIME2).</summary>
    DateTime UtcNow { get; }

    /// <summary>UTC to Asia/Ho_Chi_Minh local time (shift times are local time).</summary>
    DateTime ToLocal(DateTime utc);

    /// <summary>Asia/Ho_Chi_Minh local time to UTC.</summary>
    DateTime ToUtc(DateTime local);

    /// <summary>Today's date in Asia/Ho_Chi_Minh.</summary>
    DateOnly LocalToday { get; }
}
