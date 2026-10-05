using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Customers;

/// <summary>
/// Repository contract for FAVORITE_WORKER (Customers module, BE-M1-06).
/// Every operation is scoped by customer id, and add / remove are idempotent by contract (customers.md §2.3),
/// so they persist on their own instead of going through IUnitOfWork: a duplicate key from a concurrent
/// double-click has to be absorbed where the database exception is visible.
/// </summary>
public interface IFavoriteWorkerRepository
{
    /// <summary>The customer's favorites, newest first (read-only).</summary>
    Task<IReadOnlyList<FavoriteWorker>> ListByCustomerAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the pair when missing and returns the stored row. When the pair already exists, including when a
    /// concurrent request inserted it first, the existing row (original created_at) is returned and nothing changes.
    /// </summary>
    Task<FavoriteWorker> AddIfMissingAsync(int customerId, int workerId, DateTime createdAt, CancellationToken cancellationToken = default);

    /// <summary>Removes the customer's own row for the worker. Removing a missing pair is not an error.</summary>
    Task RemoveAsync(int customerId, int workerId, CancellationToken cancellationToken = default);
}
