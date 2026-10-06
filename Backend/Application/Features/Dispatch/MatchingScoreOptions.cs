namespace CommonService.Application.Features.Dispatch;

/// <summary>
/// Configuration options and weights for the pure function MatchingScore algorithm (PRD §2.1, decisions Q14).
/// </summary>
public sealed class MatchingScoreOptions
{
    public const string SectionName = "BusinessRules:Dispatch:MatchingScore";

    /// <summary>
    /// Weight for geographical proximity (closer candidates receive higher score). Default 0.40 (40%).
    /// </summary>
    public double DistanceWeight { get; set; } = 0.40;

    /// <summary>
    /// Weight for worker average rating (1.0 to 5.0 scale). Default 0.40 (40%).
    /// </summary>
    public double RatingWeight { get; set; } = 0.40;

    /// <summary>
    /// Weight for completed jobs experience. Default 0.20 (20%).
    /// </summary>
    public double HistoryWeight { get; set; } = 0.20;

    /// <summary>
    /// Benchmark number of completed jobs for full experience score (e.g. 50 jobs = 1.0).
    /// </summary>
    public int BenchmarkCompletedJobs { get; set; } = 50;

    /// <summary>
    /// Minimum average rating required for workers with >= MinJobsForRatingFilter (decisions Q14).
    /// </summary>
    public decimal MinRatingThreshold { get; set; } = 4.00m;

    /// <summary>
    /// Minimum completed jobs count before the rating exclusion filter takes effect (decisions Q14: >= 10 jobs).
    /// </summary>
    public int MinJobsForRatingFilter { get; set; } = 10;
}
