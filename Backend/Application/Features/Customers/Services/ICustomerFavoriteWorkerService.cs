using CommonService.Application.Features.Customers.Dtos;

namespace CommonService.Application.Features.Customers.Services;

/// <summary>
/// Favorite workers ("thợ quen") of the signed-in customer (contract customers.md §2.3, BE-M1-06).
/// The customer id always comes from ICurrentUser in the controller, never from the request.
/// </summary>
public interface ICustomerFavoriteWorkerService
{
    Task<FavoriteWorkerResult<IReadOnlyList<FavoriteWorkerDto>>> ListAsync(int customerId, CancellationToken ct = default);

    /// <summary>Idempotent add; 404 when the worker does not exist.</summary>
    Task<FavoriteWorkerResult<FavoriteWorkerDto>> AddAsync(int customerId, int workerId, CancellationToken ct = default);

    /// <summary>Idempotent remove; always succeeds.</summary>
    Task<FavoriteWorkerResult<object?>> RemoveAsync(int customerId, int workerId, CancellationToken ct = default);
}
