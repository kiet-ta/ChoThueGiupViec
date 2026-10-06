using CommonService.Application.Features.Dispatch;
using Xunit;

namespace CommonService.Tests.Dispatch;

public sealed class MatchingScoreCalculatorTests
{
    [Fact]
    public void CalculateScore_WithPerfectCandidate_ReturnsMaxScore()
    {
        // 0 km distance, 5.0 rating, 50 completed jobs -> 1.0 on all 3 components
        var candidate = new DispatchCandidate
        {
            WorkerId = 1,
            WorkerType = "FREELANCER",
            DistanceKm = 0.0,
            RatingAvg = 5.0m,
            CompletedJobs = 50
        };

        var result = MatchingScoreCalculator.CalculateScore(candidate, searchRadiusKm: 5.0);

        Assert.True(result.IsEligible);
        Assert.Null(result.IneligibilityReason);
        Assert.Equal(1.0, result.DistanceScore);
        Assert.Equal(1.0, result.RatingScore);
        Assert.Equal(1.0, result.HistoryScore);
        Assert.Equal(1.0, result.TotalScore);
    }

    [Fact]
    public void CalculateScore_WithBoundaryDistance_DistanceScoreIsZero()
    {
        // Distance equals search radius (5.0 km) -> distance score should be 0.0
        var candidate = new DispatchCandidate
        {
            WorkerId = 2,
            WorkerType = "FREELANCER",
            DistanceKm = 5.0,
            RatingAvg = 5.0m,
            CompletedJobs = 50
        };

        var result = MatchingScoreCalculator.CalculateScore(candidate, searchRadiusKm: 5.0);

        Assert.True(result.IsEligible);
        Assert.Equal(0.0, result.DistanceScore);
        Assert.Equal(1.0, result.RatingScore);
        Assert.Equal(1.0, result.HistoryScore);
        // Total score = (0 * 0.4) + (1.0 * 0.4) + (1.0 * 0.2) = 0.60
        Assert.Equal(0.60, result.TotalScore);
    }

    [Fact]
    public void CalculateScore_WhenDistanceExceedsRadius_ReturnsIneligible()
    {
        // Distance 5.1 km > 5.0 km radius
        var candidate = new DispatchCandidate
        {
            WorkerId = 3,
            WorkerType = "FREELANCER",
            DistanceKm = 5.1,
            RatingAvg = 4.8m,
            CompletedJobs = 20
        };

        var result = MatchingScoreCalculator.CalculateScore(candidate, searchRadiusKm: 5.0);

        Assert.False(result.IsEligible);
        Assert.NotNull(result.IneligibilityReason);
        Assert.Contains("exceeds search radius", result.IneligibilityReason);
        Assert.Equal(0.0, result.TotalScore);
    }

    [Fact]
    public void CalculateScore_WhenRatingBelow4After10Jobs_ReturnsIneligible_Q14()
    {
        // Rating 3.95 with 12 completed jobs violates Q14 (< 4.00 after >= 10 jobs)
        var candidate = new DispatchCandidate
        {
            WorkerId = 4,
            WorkerType = "FREELANCER",
            DistanceKm = 1.0,
            RatingAvg = 3.95m,
            CompletedJobs = 12
        };

        var result = MatchingScoreCalculator.CalculateScore(candidate, searchRadiusKm: 5.0);

        Assert.False(result.IsEligible);
        Assert.NotNull(result.IneligibilityReason);
        Assert.Contains("Q14", result.IneligibilityReason);
        Assert.Equal(0.0, result.TotalScore);
    }

    [Fact]
    public void CalculateScore_WhenRatingBelow4Before10Jobs_RemainsEligible_Q14()
    {
        // Rating 3.80 with only 5 jobs (new worker probation, not excluded yet by Q14)
        var candidate = new DispatchCandidate
        {
            WorkerId = 5,
            WorkerType = "FREELANCER",
            DistanceKm = 1.0,
            RatingAvg = 3.80m,
            CompletedJobs = 5
        };

        var result = MatchingScoreCalculator.CalculateScore(candidate, searchRadiusKm: 5.0);

        Assert.True(result.IsEligible);
        Assert.Null(result.IneligibilityReason);
        Assert.True(result.TotalScore > 0);
    }

    [Fact]
    public void CalculateScore_CustomWeights_ReflectsCustomConfiguration()
    {
        var candidate = new DispatchCandidate
        {
            WorkerId = 6,
            WorkerType = "FREELANCER",
            DistanceKm = 2.5, // 2.5 / 5.0 -> 50% distance score
            RatingAvg = 5.0m, // (5.0 - 1.0) / 4.0 -> 100% rating score
            CompletedJobs = 25 // 25 / 50 -> 50% history score
        };

        // Custom weights: 0.50, 0.30, 0.20
        var options = new MatchingScoreOptions
        {
            DistanceWeight = 0.50,
            RatingWeight = 0.30,
            HistoryWeight = 0.20,
            BenchmarkCompletedJobs = 50
        };

        var result = MatchingScoreCalculator.CalculateScore(candidate, searchRadiusKm: 5.0, options);

        Assert.True(result.IsEligible);
        Assert.Equal(0.50, result.DistanceScore);
        Assert.Equal(1.00, result.RatingScore);
        Assert.Equal(0.50, result.HistoryScore);
        // Total = (0.5 * 0.5) + (1.0 * 0.3) + (0.5 * 0.2) = 0.25 + 0.30 + 0.10 = 0.65
        Assert.Equal(0.65, result.TotalScore);
    }

    [Fact]
    public void CalculateScore_NegativeDistance_ThrowsArgumentException()
    {
        var candidate = new DispatchCandidate
        {
            WorkerId = 7,
            WorkerType = "FREELANCER",
            DistanceKm = -1.0,
            RatingAvg = 4.5m,
            CompletedJobs = 10
        };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MatchingScoreCalculator.CalculateScore(candidate, searchRadiusKm: 5.0));
    }

    [Fact]
    public void CalculateScore_ZeroOrNegativeRadius_ThrowsArgumentException()
    {
        var candidate = new DispatchCandidate
        {
            WorkerId = 8,
            WorkerType = "FREELANCER",
            DistanceKm = 1.0,
            RatingAvg = 4.5m,
            CompletedJobs = 10
        };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MatchingScoreCalculator.CalculateScore(candidate, searchRadiusKm: 0.0));
    }

    [Fact]
    public void RankCandidates_OrdersCandidatesByTotalScoreDescending()
    {
        var candidates = new List<DispatchCandidate>
        {
            // Close, high rating, experienced
            new() { WorkerId = 10, WorkerType = "FREELANCER", DistanceKm = 1.0, RatingAvg = 4.9m, CompletedJobs = 40 },
            // Far, lower rating
            new() { WorkerId = 20, WorkerType = "FREELANCER", DistanceKm = 4.5, RatingAvg = 4.2m, CompletedJobs = 10 },
            // Medium
            new() { WorkerId = 30, WorkerType = "FREELANCER", DistanceKm = 2.0, RatingAvg = 4.6m, CompletedJobs = 25 },
        };

        var ranked = MatchingScoreCalculator.RankCandidates(candidates, searchRadiusKm: 5.0);

        Assert.Equal(3, ranked.Count);
        Assert.Equal(10, ranked[0].Candidate.WorkerId);
        Assert.Equal(30, ranked[1].Candidate.WorkerId);
        Assert.Equal(20, ranked[2].Candidate.WorkerId);
    }

    [Fact]
    public void RankCandidates_FiltersOutIneligibleCandidates()
    {
        var candidates = new List<DispatchCandidate>
        {
            // Eligible
            new() { WorkerId = 10, WorkerType = "FREELANCER", DistanceKm = 2.0, RatingAvg = 4.5m, CompletedJobs = 20 },
            // Ineligible (Q14 violation: < 4.00 after >= 10 jobs)
            new() { WorkerId = 20, WorkerType = "FREELANCER", DistanceKm = 1.0, RatingAvg = 3.8m, CompletedJobs = 15 },
            // Ineligible (out of radius: 6.0km > 5.0km)
            new() { WorkerId = 30, WorkerType = "FREELANCER", DistanceKm = 6.0, RatingAvg = 5.0m, CompletedJobs = 50 },
        };

        var ranked = MatchingScoreCalculator.RankCandidates(candidates, searchRadiusKm: 5.0);

        Assert.Single(ranked);
        Assert.Equal(10, ranked[0].Candidate.WorkerId);
    }

    [Fact]
    public void RankCandidates_TieBreaker_PrefersShorterDistance()
    {
        // Equal total score candidates
        var candidates = new List<DispatchCandidate>
        {
            new() { WorkerId = 100, WorkerType = "FREELANCER", DistanceKm = 2.0, RatingAvg = 5.0m, CompletedJobs = 50 },
            new() { WorkerId = 200, WorkerType = "FREELANCER", DistanceKm = 1.0, RatingAvg = 5.0m, CompletedJobs = 50 },
        };

        // Candidate 200 is closer (1.0 km vs 2.0 km) -> higher distance score -> higher total score
        var ranked = MatchingScoreCalculator.RankCandidates(candidates, searchRadiusKm: 5.0);

        Assert.Equal(200, ranked[0].Candidate.WorkerId);
        Assert.Equal(100, ranked[1].Candidate.WorkerId);
    }
}
