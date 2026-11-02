namespace CommonService.Application.Interfaces.Ports;

/// <summary>
/// Read access to the customer's favorite workers for other modules (Booking checks a <c>preferredWorkerId</c>, booking.md 3.3).
/// Nobody outside Customers reads FAVORITE_WORKER itself. Implemented by Customers (M1).
/// </summary>
public interface ICustomerFavoriteQuery
{
    /// <summary>True when the worker is in the customer's favorites.</summary>
    Task<bool> IsFavoriteAsync(int customerId, int workerId, CancellationToken cancellationToken = default);
}
