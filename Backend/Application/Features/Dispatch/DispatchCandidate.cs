namespace CommonService.Application.Features.Dispatch;

/// <summary>
/// Candidate worker input model for dispatch matching score computation.
/// </summary>
public sealed record DispatchCandidate
{
    public required long WorkerId { get; init; }
    public required string WorkerType { get; init; } // "FREELANCER" or "AGENCY"
    public long? AgencyId { get; init; }
    public required double DistanceKm { get; init; }
    public required decimal RatingAvg { get; init; }
    public required int CompletedJobs { get; init; }
    public int CanceledJobsCount { get; init; } = 0;
}
