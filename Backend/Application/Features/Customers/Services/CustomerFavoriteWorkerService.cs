using CommonService.Application.Features.Customers.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Customers.Services;

/// <summary>
/// Implements the favorite-workers rules of contract customers.md §2.3 (BE-M1-06). Worker display data and the
/// existence check come only from the read port IWorkerProfileQuery (decisions Q21 C3); this module never reads WORKER.
/// </summary>
public sealed class CustomerFavoriteWorkerService : ICustomerFavoriteWorkerService
{
    private readonly ICustomerRepository _customers;
    private readonly IFavoriteWorkerRepository _favorites;
    private readonly IWorkerProfileQuery _workers;
    private readonly IClock _clock;

    public CustomerFavoriteWorkerService(
        ICustomerRepository customers,
        IFavoriteWorkerRepository favorites,
        IWorkerProfileQuery workers,
        IClock clock)
    {
        _customers = customers;
        _favorites = favorites;
        _workers = workers;
        _clock = clock;
    }

    public async Task<FavoriteWorkerResult<IReadOnlyList<FavoriteWorkerDto>>> ListAsync(int customerId, CancellationToken ct = default)
    {
        var rows = await _favorites.ListByCustomerAsync(customerId, ct);
        if (rows.Count == 0)
        {
            return FavoriteWorkerResult<IReadOnlyList<FavoriteWorkerDto>>.Ok([]);
        }

        // One batched call to the port, not one per row.
        var profiles = await _workers.GetManyAsync(rows.Select(r => r.WorkerId), ct);

        // A favorite whose worker the port does not return cannot be displayed and is left out.
        var list = rows
            .Where(r => profiles.ContainsKey(r.WorkerId))
            .Select(r => ToDto(profiles[r.WorkerId], r))
            .ToList();

        return FavoriteWorkerResult<IReadOnlyList<FavoriteWorkerDto>>.Ok(list);
    }

    public async Task<FavoriteWorkerResult<FavoriteWorkerDto>> AddAsync(int customerId, int workerId, CancellationToken ct = default)
    {
        var profile = await _workers.GetAsync(workerId, ct);
        if (profile == null)
        {
            return FavoriteWorkerResult<FavoriteWorkerDto>.NotFound("Worker not found.");
        }

        if (await _customers.GetByIdAsync(customerId, ct) == null)
        {
            return FavoriteWorkerResult<FavoriteWorkerDto>.NotFound("Customer not found.");
        }

        var row = await _favorites.AddIfMissingAsync(customerId, workerId, _clock.UtcNow, ct);
        return FavoriteWorkerResult<FavoriteWorkerDto>.Ok(ToDto(profile, row));
    }

    public async Task<FavoriteWorkerResult<object?>> RemoveAsync(int customerId, int workerId, CancellationToken ct = default)
    {
        await _favorites.RemoveAsync(customerId, workerId, ct);
        return FavoriteWorkerResult<object?>.Ok(null);
    }

    private static FavoriteWorkerDto ToDto(WorkerProfileSummary profile, FavoriteWorker row) => new()
    {
        WorkerId = profile.WorkerId,
        FullName = profile.FullName,
        RatingAvg = profile.RatingAvg,
        CompletedJobs = profile.CompletedJobs,
        WorkStatus = DbEnum.ToDb(profile.WorkStatus),
        AddedAt = row.CreatedAt
    };
}
