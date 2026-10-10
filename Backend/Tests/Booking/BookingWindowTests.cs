using CommonService.Application.Features.Booking;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using Xunit;

namespace CommonService.Tests.Booking;

/// <summary>BE-M2-03, pure parts: shift windows (B1), order_code (B7), future / Premium lead-time rules (contract 3.3 rules 3-4).</summary>
public sealed class BookingWindowTests
{
    private sealed class VietnamClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
        public DateTime ToLocal(DateTime utc) => utc.AddHours(7);
        public DateTime ToUtc(DateTime local) => DateTime.SpecifyKind(local.AddHours(-7), DateTimeKind.Utc);
        public DateOnly LocalToday => DateOnly.FromDateTime(ToLocal(utcNow));
    }

    private static readonly DateOnly Date = new(2026, 10, 15);

    [Theory]
    [InlineData(BookingShifts.Morning, 8, 0, 12, 0)]
    [InlineData(BookingShifts.Afternoon, 13, 0, 17, 0)]
    [InlineData(BookingShifts.Evening, 17, 30, 20, 30)]
    public void GetLocalTimes_ReturnsTheContractShiftWindows(string code, int sh, int sm, int eh, int em)
    {
        var times = BookingShifts.GetLocalTimes(code);

        Assert.Equal((new TimeOnly(sh, sm), new TimeOnly(eh, em)), times);
    }

    [Theory]
    [InlineData("SANG")]
    [InlineData("MORNING")]
    [InlineData("shift_morning")]
    [InlineData("")]
    [InlineData(null)]
    public void GetLocalTimes_RejectsAnyOtherCode(string? code) => Assert.Null(BookingShifts.GetLocalTimes(code));

    [Fact]
    public void GetShiftStartUtc_ConvertsTheLocalStartThroughTheClock()
    {
        var clock = new VietnamClock(new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc));

        var start = BookingWindowPolicy.GetShiftStartUtc(Date, BookingShifts.Morning, clock);

        Assert.Equal(new DateTime(2026, 10, 15, 1, 0, 0, DateTimeKind.Utc), start);
        Assert.Null(BookingWindowPolicy.GetShiftStartUtc(Date, "SANG", clock));
    }

    private static readonly DateTime MorningStartUtc = new(2026, 10, 15, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Check_Premium_AllowsExactlyFourHoursAhead()
    {
        var error = BookingWindowPolicy.Check(ServiceTier.Premium, MorningStartUtc, MorningStartUtc.AddHours(-4), 4);

        Assert.Null(error);
    }

    [Fact]
    public void Check_Premium_RejectsOneMinuteInsideTheLeadTime()
    {
        var error = BookingWindowPolicy.Check(ServiceTier.Premium, MorningStartUtc, MorningStartUtc.AddHours(-4).AddMinutes(1), 4);

        Assert.Equal(BookingErrorCodes.PremiumLeadTime, error);
    }

    [Fact]
    public void Check_Economy_HasNoLeadTime_OnlyTheFutureRule()
    {
        Assert.Null(BookingWindowPolicy.Check(ServiceTier.Economy, MorningStartUtc, MorningStartUtc.AddMinutes(-1), 4));
        Assert.Equal(BookingErrorCodes.ShiftInPast, BookingWindowPolicy.Check(ServiceTier.Economy, MorningStartUtc, MorningStartUtc, 4));
        Assert.Equal(BookingErrorCodes.ShiftInPast, BookingWindowPolicy.Check(ServiceTier.Economy, MorningStartUtc, MorningStartUtc.AddHours(1), 4));
    }

    [Fact]
    public void Check_Premium_InThePast_IsShiftInPast_NotLeadTime()
    {
        var error = BookingWindowPolicy.Check(ServiceTier.Premium, MorningStartUtc, MorningStartUtc.AddHours(1), 4);

        Assert.Equal(BookingErrorCodes.ShiftInPast, error);
    }

    [Fact]
    public void Check_UsesTheConfiguredLeadHours()
    {
        Assert.Null(BookingWindowPolicy.Check(ServiceTier.Premium, MorningStartUtc, MorningStartUtc.AddHours(-6), 6));
        Assert.Equal(BookingErrorCodes.PremiumLeadTime, BookingWindowPolicy.Check(ServiceTier.Premium, MorningStartUtc, MorningStartUtc.AddHours(-5), 6));
    }

    [Fact]
    public void OrderCode_IsGvPlusLocalDatePlusSixAlphanumerics_Within20Chars()
    {
        var sequence = new Queue<int>([10, 19, 15, 25, 16, 35]); // K T P Z Q 9
        var code = OrderCodeGenerator.Create(Date, _ => sequence.Dequeue());

        Assert.Equal("GV261015KTPZQ9", code);
        Assert.Equal(14, code.Length);
        Assert.True(code.Length <= 20);
    }

    [Fact]
    public void OrderCode_OnlyUsesUppercaseLettersAndDigits_AndVaries()
    {
        var random = new Random(1);
        var codes = Enumerable.Range(0, 200).Select(_ => OrderCodeGenerator.Create(Date, random.Next)).ToList();

        Assert.All(codes, c => Assert.Matches("^GV261015[A-Z0-9]{6}$", c));
        Assert.True(codes.Distinct().Count() > 190);
    }

    [Fact]
    public void OrderCode_RejectsAMissingRandomSource() =>
        Assert.Throws<ArgumentNullException>(() => OrderCodeGenerator.Create(Date, null!));
}
