using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Features.Workers.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using Xunit;

namespace CommonService.Tests.Workers;

/// <summary>
/// MOB-SLOTS-ERR-01 (#814): a shift of today whose start time has passed (Vietnam local time) is refused with 409 SHIFT_ALREADY_STARTED
/// before the database is touched, so these tests pass a null repository: reaching it would be a failure of the rule itself.
/// </summary>
public class BookingSlotStartedShiftTests
{
    private static readonly DateOnly Today = new(2026, 11, 12);

    private class User : ICurrentUser
    {
        public int? UserId => 7;
        public UserRole? Role => UserRole.Worker;
        public bool IsAuthenticated => true;
    }

    /// <summary>Clock pinned to the given Vietnam local time of <see cref="Today"/> (UTC+7).</summary>
    private static WorkerBookingSlotService Service(int localHour, int localMinute)
    {
        var clock = new FakeClock();
        clock.Set(new DateTime(Today.Year, Today.Month, Today.Day, localHour, localMinute, 0, DateTimeKind.Utc).AddHours(-7));
        return new WorkerBookingSlotService(null!, new User(), clock);
    }

    [Theory]
    [InlineData("SHIFT_MORNING", true)]
    [InlineData("SHIFT_MORNING", false)]
    [InlineData("SHIFT_AFTERNOON", true)]
    [InlineData("SHIFT_EVENING", true)]
    [InlineData("SHIFT_EVENING", false)]
    public async Task At_19_15_every_shift_of_today_is_refused_for_on_and_off(string shift, bool on)
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            Service(19, 15).ToggleAsync(new ToggleBookingSlotRequest(Today, shift, on)));

        Assert.Equal(WorkerBookingSlotService.ShiftAlreadyStartedCode, ex.Code);
    }

    [Fact]
    public async Task A_shift_is_refused_from_its_exact_start_time()
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            Service(8, 0).ToggleAsync(new ToggleBookingSlotRequest(Today, "SHIFT_MORNING", true)));

        Assert.Equal(WorkerBookingSlotService.ShiftAlreadyStartedCode, ex.Code);
    }

    [Theory]
    [InlineData(7, 59, "SHIFT_MORNING")]
    [InlineData(12, 30, "SHIFT_AFTERNOON")]
    [InlineData(17, 29, "SHIFT_EVENING")]
    public async Task A_shift_that_has_not_started_is_not_refused_by_this_rule(int hour, int minute, string shift)
    {
        // The (null) repository is reached, so a NullReferenceException proves the started-shift rule let the request through.
        var thrown = await Record.ExceptionAsync(() =>
            Service(hour, minute).ToggleAsync(new ToggleBookingSlotRequest(Today, shift, true)));

        Assert.IsNotType<BusinessRuleViolationException>(thrown);
    }

    [Fact]
    public async Task A_later_day_is_not_refused_by_this_rule_even_late_in_the_evening()
    {
        var thrown = await Record.ExceptionAsync(() =>
            Service(23, 0).ToggleAsync(new ToggleBookingSlotRequest(Today.AddDays(1), "SHIFT_MORNING", true)));

        Assert.IsNotType<BusinessRuleViolationException>(thrown);
    }

    [Fact]
    public async Task Yesterday_is_still_a_validation_error()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            Service(10, 0).ToggleAsync(new ToggleBookingSlotRequest(Today.AddDays(-1), "SHIFT_MORNING", true)));
    }
}
