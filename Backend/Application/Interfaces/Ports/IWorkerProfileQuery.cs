using CommonService.Domain.Enums;

namespace CommonService.Application.Interfaces.Ports;

/// <summary>Worker display fields for the favorite-workers list (decisions Q21 C3).</summary>
public sealed record WorkerProfileSummary(int WorkerId, string FullName, decimal RatingAvg, int CompletedJobs, WorkStatus WorkStatus);

/// <summary>Read port over the Workers module. Implemented by Workers (M4), Fake first; consumed by the favorite-workers list of Customers (M1).</summary>
public interface IWorkerProfileQuery
{
    /// <summary>Null when the worker does not exist.</summary>
    Task<WorkerProfileSummary?> GetAsync(int workerId, CancellationToken cancellationToken = default);

    /// <summary>Unknown ids are simply missing from the result.</summary>
    Task<IReadOnlyDictionary<int, WorkerProfileSummary>> GetManyAsync(IEnumerable<int> workerIds, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(int workerId, CancellationToken cancellationToken = default);
}
