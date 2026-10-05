using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CommonService.Infrastructure.Persistence.Converters;

/// <summary>
/// Enforces decisions G-3: Every stored and read timestamp must be UTC (DATETIME2).
/// Rejects non-UTC values on write, and tags DateTimeKind.Utc on read.
/// </summary>
public class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter() : base(
        toDb => EnsureUtc(toDb),
        fromDb => DateTime.SpecifyKind(fromDb, DateTimeKind.Utc))
    {
    }

    public static DateTime EnsureUtc(DateTime dt)
    {
        if (dt.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException($"DateTime must have Kind=Utc to be persisted, but got {dt.Kind} ({dt:O}).");
        }
        return dt;
    }
}

public class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
{
    public NullableUtcDateTimeConverter() : base(
        toDb => EnsureNullableUtc(toDb),
        fromDb => fromDb.HasValue ? DateTime.SpecifyKind(fromDb.Value, DateTimeKind.Utc) : null)
    {
    }

    public static DateTime? EnsureNullableUtc(DateTime? dt)
    {
        if (dt.HasValue && dt.Value.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException($"DateTime must have Kind=Utc to be persisted, but got {dt.Value.Kind} ({dt.Value:O}).");
        }
        return dt;
    }
}
