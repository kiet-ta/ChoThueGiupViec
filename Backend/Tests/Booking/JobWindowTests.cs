using CommonService.Application.Common.Options;
using CommonService.Application.Features.Booking;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Modules.Workers;
using Xunit;

namespace CommonService.Tests.Booking;

/// <summary>Decisions D34 (BOOK-TIME-01): the pure rules of a timed job (window, overlap with the travel buffer, suggestion, price, day cap).</summary>
public class JobWindowTests
{
    private static readonly BusinessRules Rules = new();

    private static (TimeOnly, TimeOnly) W(int h1, int m1, int h2, int m2) => (new TimeOnly(h1, m1), new TimeOnly(h2, m2));

    [Fact]
    public void OfOrder_UsesTheOrderTimes_ElseTheShift()
    {
        Assert.Equal(W(9, 0, 11, 0), JobWindow.OfOrder(BookingShifts.Morning, new TimeOnly(9, 0), new TimeOnly(11, 0)));
        Assert.Equal(W(8, 0, 12, 0), JobWindow.OfOrder(BookingShifts.Morning, null, null));
        Assert.Null(JobWindow.OfOrder("NOPE", null, null));
    }

    [Fact]
    public void OfAssignment_InAnotherShift_IsThatWholeShift()
    {
        // BR-02 fallback: the second assignment sits in the next shift's slot.
        var window = JobWindow.OfAssignment(BookingShifts.Morning, new TimeOnly(9, 0), new TimeOnly(11, 0),
            BookingShifts.Afternoon, new TimeOnly(13, 0), new TimeOnly(17, 0));
        Assert.Equal(W(13, 0, 17, 0), window);
    }

    [Theory]
    [InlineData(8, 0, 10, 0, 10, 30, 12, 0, false)] // exactly the 30 min buffer apart
    [InlineData(8, 0, 10, 0, 10, 0, 12, 0, true)]   // back to back, no travel time
    [InlineData(8, 0, 10, 0, 10, 15, 12, 0, true)]  // inside the buffer
    [InlineData(8, 0, 10, 0, 9, 0, 11, 0, true)]    // plain overlap
    [InlineData(10, 30, 12, 0, 8, 0, 10, 0, false)] // same, other order
    public void Overlaps_CountsTheTravelBuffer(int a1, int a2, int a3, int a4, int b1, int b2, int b3, int b4, bool expected) =>
        Assert.Equal(expected, JobWindow.Overlaps(W(a1, a2, a3, a4), W(b1, b2, b3, b4), 30));

    [Theory]
    [InlineData(20, 1, 2)]     // 1 h of work: the 2 h minimum
    [InlineData(50, 1, 2.5)]   // 50 / 20 = 2.5 h
    [InlineData(55, 1, 3)]     // 2.75 h rounded up to the half hour
    [InlineData(80, 1, 4)]     // the standard maximum: a full 4 h
    [InlineData(120, 2, 3)]    // 60 m2 each -> 3 h
    [InlineData(400, 2, 4)]    // capped at the shift maximum
    public void SuggestedHours_FollowsTheAreaPerWorker(double area, int workers, double expected) =>
        Assert.Equal((decimal)expected, JobWindow.SuggestedHours((decimal)area, workers, Rules));

    [Fact]
    public void Validate_GivesTheWindow_OrTheFieldError()
    {
        var ok = JobWindow.Validate(BookingShifts.Evening, new TimeOnly(17, 30), 3m, Rules);
        Assert.Empty(ok.Errors);
        Assert.Equal(W(17, 30, 20, 30), ok.Window);

        var tooLong = JobWindow.Validate(BookingShifts.Evening, new TimeOnly(17, 30), 4m, Rules); // the evening is only 3 h
        Assert.Contains("startTime", tooLong.Errors.Keys);
        Assert.Null(tooLong.Window);

        var none = JobWindow.Validate(BookingShifts.Morning, null, null, Rules);
        Assert.Empty(none.Errors);
        Assert.Null(none.Window);
    }

    [Fact]
    public void ForHours_PricesTheShiftRowPerHour()
    {
        var shift = PriceCalculator.Calculate(ServiceTier.Economy, 50m, 2, 260000m);

        var quote = PriceCalculator.ForHours(shift, 2.5m, 4);

        Assert.Equal(325000m, quote.TotalAmount); // 162,500 x 2
        Assert.Equal(260000m, quote.UnitPrice);
    }

    [Fact]
    public void TheDefaultTravelBufferIsZero_SoTouchingJobsDoNotOverlap()
    {
        // SHIFT-MULTI-01 (decisions D42): a 2 h shortest job and a 4 h shift only fit two jobs when they may touch.
        Assert.Equal(0, Rules.Booking.TravelBufferMinutes);
        Assert.False(JobWindow.Overlaps(W(8, 0, 10, 0), W(10, 0, 12, 0), Rules.Booking.TravelBufferMinutes));
        Assert.True(JobWindow.Overlaps(W(8, 0, 10, 0), W(9, 45, 12, 0), Rules.Booking.TravelBufferMinutes));
    }

    [Fact]
    public void Fits_AcceptsTwoJobsOneAfterTheOtherInOneShift_AndRefusesAnOverlap()
    {
        var jobs = new List<(TimeOnly, TimeOnly)> { W(8, 0, 10, 0) };
        Assert.True(WorkerJobTimes.Fits(jobs, W(10, 0, 12, 0), Rules));
        Assert.False(WorkerJobTimes.Fits(jobs, W(9, 0, 11, 0), Rules));
        Assert.False(WorkerJobTimes.Fits(jobs, W(8, 0, 10, 0), Rules));
    }

    [Fact]
    public void Fits_StillKeepsAGapWhenTheBufferIsRaised()
    {
        var withGap = new BusinessRules();
        withGap.Booking.TravelBufferMinutes = 30;
        var jobs = new List<(TimeOnly, TimeOnly)> { W(8, 0, 10, 0) };

        Assert.True(WorkerJobTimes.Fits(jobs, W(10, 30, 12, 0), withGap));
        Assert.False(WorkerJobTimes.Fits(jobs, W(10, 0, 12, 0), withGap));
    }

    [Fact]
    public void Fits_RefusesADayOverTheCap()
    {

        // 4 h + 3 h already that day: one more hour is the 8 h cap, two more is over it.
        var day = new List<(TimeOnly, TimeOnly)> { W(8, 0, 12, 0), W(13, 0, 16, 0) };
        Assert.True(WorkerJobTimes.Fits(day, W(17, 30, 18, 30), Rules));
        Assert.False(WorkerJobTimes.Fits(day, W(17, 30, 19, 30), Rules));
    }
}
