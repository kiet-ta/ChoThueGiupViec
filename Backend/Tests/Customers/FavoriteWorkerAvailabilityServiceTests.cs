using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Customers.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Infrastructure.Fakes;
using Xunit;

namespace CommonService.Tests.Customers;

/// <summary>customers.md 2.3: the free shifts of a favorite worker, for the booking form.</summary>
public sealed class FavoriteWorkerAvailabilityServiceTests
{
    private const int CustomerId = 7;
    private const int FavoriteId = 42;
    private static readonly DateTime Now = new(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc); // 10:00 on 14/10 in Ho Chi Minh
    private static readonly DateOnly Today = new(2026, 10, 14);

    private sealed class StubFavorites : ICustomerFavoriteQuery
    {
        public Task<bool> IsFavoriteAsync(int customerId, int workerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(customerId == CustomerId && workerId == FavoriteId);
    }

    private sealed class StubShifts : IWorkerFreeShiftQuery
    {
        public List<(int WorkerId, DateOnly From, DateOnly To)> Calls { get; } = [];

        public Task<IReadOnlyList<WorkerFreeShift>> GetFreeShiftsAsync(
            int workerId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            Calls.Add((workerId, from, to));
            IReadOnlyList<WorkerFreeShift> free =
            [
                new(new DateOnly(2026, 10, 15), "SHIFT_MORNING"),
                new(new DateOnly(2026, 10, 16), "SHIFT_EVENING"),
            ];
            return Task.FromResult(free);
        }
    }

    private static (FavoriteWorkerAvailabilityService Service, StubShifts Shifts) Create()
    {
        var clock = new FakeClock();
        clock.Set(Now);
        var shifts = new StubShifts();
        var service = new FavoriteWorkerAvailabilityService(
            new StubFavorites(), shifts, clock, Microsoft.Extensions.Options.Options.Create(new BusinessRules()));
        return (service, shifts);
    }

    [Fact]
    public async Task Favorite_ReturnsTheFreeShifts_OverTheBookingWindowByDefault()
    {
        var (service, shifts) = Create();

        var free = await service.GetFreeShiftsAsync(CustomerId, FavoriteId, null, null);

        Assert.Equal(2, free.Count);
        Assert.Equal(new DateOnly(2026, 10, 15), free[0].Date);
        Assert.Equal("SHIFT_MORNING", free[0].ShiftCode);
        Assert.Equal((FavoriteId, Today, Today.AddDays(14)), Assert.Single(shifts.Calls));
    }

    [Fact]
    public async Task NotAFavorite_IsNotFound_AndNoCalendarIsRead()
    {
        var (service, shifts) = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetFreeShiftsAsync(CustomerId, 99, null, null));

        Assert.Empty(shifts.Calls);
    }

    [Fact]
    public async Task ToBeforeFrom_IsAValidationError()
    {
        var (service, _) = Create();

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.GetFreeShiftsAsync(CustomerId, FavoriteId, Today.AddDays(3), Today));

        Assert.True(error.Errors.ContainsKey("to"));
    }

    [Fact]
    public async Task ARangeLongerThanTheBookingWindow_IsAValidationError()
    {
        var (service, _) = Create();

        await Assert.ThrowsAsync<ValidationException>(
            () => service.GetFreeShiftsAsync(CustomerId, FavoriteId, Today, Today.AddDays(15)));
    }
}
