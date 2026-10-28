using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Customers.Dtos;
using CommonService.Application.Interfaces.Ports;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Customers.Services;

public interface IFavoriteWorkerAvailabilityService
{
    /// <summary>
    /// GET /api/customers/me/favorite-workers/{workerId}/availability (contract customers.md §2.3): the free shifts of one of the
    /// caller's favorite workers. NotFoundException (404) when the worker is not in the caller's favorites; ValidationException (400)
    /// on a bad range.
    /// </summary>
    Task<IReadOnlyList<FavoriteWorkerFreeShiftDto>> GetFreeShiftsAsync(
        int customerId, int workerId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default);
}

/// <summary>
/// Free shifts of a favorite worker. Only a favorite can be looked up (a customer does not browse other workers' calendars); the range
/// defaults to today .. today + Booking.MaxDaysAhead, the days the booking form offers. Slots come from the Workers port
/// <see cref="IWorkerFreeShiftQuery"/>; this module never reads BOOKING_SLOT.
/// </summary>
public sealed class FavoriteWorkerAvailabilityService(
    ICustomerFavoriteQuery favorites,
    IWorkerFreeShiftQuery shifts,
    IClock clock,
    IOptions<BusinessRules> rules) : IFavoriteWorkerAvailabilityService
{
    private readonly BusinessRules _rules = rules.Value;

    public async Task<IReadOnlyList<FavoriteWorkerFreeShiftDto>> GetFreeShiftsAsync(
        int customerId, int workerId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var today = clock.LocalToday;
        var start = from ?? today;
        var end = to ?? today.AddDays(_rules.Booking.MaxDaysAhead);

        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (end < start)
        {
            errors["to"] = ["to must be on or after from."];
        }
        else if (end.DayNumber - start.DayNumber > _rules.Booking.MaxDaysAhead)
        {
            errors["to"] = [$"The range must be at most {_rules.Booking.MaxDaysAhead} days."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        if (!await favorites.IsFavoriteAsync(customerId, workerId, cancellationToken))
        {
            throw new NotFoundException("FavoriteWorker", workerId);
        }

        var free = await shifts.GetFreeShiftsAsync(workerId, start, end, cancellationToken);
        return free.Select(s => new FavoriteWorkerFreeShiftDto { Date = s.Date, ShiftCode = s.ShiftCode }).ToList();
    }
}
