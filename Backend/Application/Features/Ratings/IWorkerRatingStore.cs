using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Ratings;

/// <summary>What <see cref="IWorkerRatingStore.SetAverageAsync"/> found on the worker row before it changed it.</summary>
public sealed record WorkerAverageChange(decimal PreviousAverage, WorkerType WorkerType);

/// <summary>One rating that a worker gave a customer (TWO_WAY_RATING, rater_role WORKER).</summary>
public sealed record RecentCustomerRating(int WorkerId, byte Stars);

/// <summary>
/// The two things the rating handlers need besides <see cref="IRatingRepository"/> (BE-M6 ADMIN-UAT-10): the rating average column of a worker
/// and the latest ratings a customer received. The Ratings module owns both reads; the column is only written here.
/// </summary>
public interface IWorkerRatingStore
{
    /// <summary>
    /// Sets <c>WORKER.rating_avg</c> on the TRACKED row and returns what it was; the caller saves it in the same unit of work as its
    /// audit row. Null when the worker does not exist. Dispatch, the favourite list, order tracking and the profile read this column.
    /// </summary>
    Task<WorkerAverageChange?> SetAverageAsync(int workerId, decimal average, CancellationToken cancellationToken = default);

    /// <summary>The newest ratings that WORKERS gave this customer, newest first, at most <paramref name="take"/>.</summary>
    Task<IReadOnlyList<RecentCustomerRating>> GetRecentRatingsOfCustomerAsync(int customerId, int take, CancellationToken cancellationToken = default);
}
