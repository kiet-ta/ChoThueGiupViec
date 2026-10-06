namespace CommonService.Application.Features.Dispatch;

/// <summary>
/// Evaluation result of candidate worker matching score.
/// </summary>
public sealed record MatchingScoreResult
{
    public required long WorkerId { get; init; }
    public required double TotalScore { get; init; }
    public required double DistanceScore { get; init; }
    public required double RatingScore { get; init; }
    public required double HistoryScore { get; init; }
    public required bool IsEligible { get; init; }
    public string? IneligibilityReason { get; init; }
}
