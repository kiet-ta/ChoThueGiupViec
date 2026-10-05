namespace CommonService.Application.Interfaces.Ports;

/// <param name="RatingAvg">0.00 - 5.00 (decisions Q14).</param>
/// <param name="SuccessRate">Share of accepted jobs that ended COMPLETED, 0 - 1.</param>
public sealed record WorkerReputationDto(int WorkerId, decimal RatingAvg, int CompletedJobs, decimal SuccessRate);

/// <summary>Rating and success rate used by the MatchingScore. Implemented by Ratings (M6); consumed by Dispatch (M3).</summary>
public interface IWorkerReputation
{
    /// <summary>Null when the worker is unknown.</summary>
    Task<WorkerReputationDto?> GetAsync(int workerId, CancellationToken cancellationToken = default);
}
