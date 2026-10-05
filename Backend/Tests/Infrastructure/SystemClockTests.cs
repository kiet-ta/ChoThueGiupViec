using CommonService.Infrastructure.Services;

namespace CommonService.Tests.Infrastructure;

public class SystemClockTests
{
    private readonly SystemClock _clock = new();

    [Fact]
    public void UtcNow_is_close_to_system_utc()
    {
        var before = DateTime.UtcNow;
        var now = _clock.UtcNow;
        var after = DateTime.UtcNow;

        Assert.True(now >= before && now <= after);
    }

    [Fact]
    public void ToLocal_adds_7_hours_to_utc()
    {
        var utc = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        var local = _clock.ToLocal(utc);

        Assert.Equal(new DateTime(2026, 10, 5, 19, 0, 0), local);
    }

    [Fact]
    public void ToUtc_subtracts_7_hours_from_local()
    {
        var local = new DateTime(2026, 10, 5, 19, 0, 0, DateTimeKind.Unspecified);
        var utc = _clock.ToUtc(local);

        Assert.Equal(new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc), utc);
    }

    [Fact]
    public void Round_trip_utc_to_local_and_back()
    {
        var originalUtc = new DateTime(2026, 1, 15, 8, 30, 45, DateTimeKind.Utc);
        var local = _clock.ToLocal(originalUtc);
        var recoveredUtc = _clock.ToUtc(local);

        Assert.Equal(originalUtc, recoveredUtc);
    }

    [Fact]
    public void LocalToday_matches_date_of_ToLocal_UtcNow()
    {
        var expectedDate = DateOnly.FromDateTime(_clock.ToLocal(_clock.UtcNow));
        var actualDate = _clock.LocalToday;

        Assert.Equal(expectedDate, actualDate);
    }
}
