namespace CommonService.Application.Features.Dispatch;

/// <summary>
/// Pure function calculator for worker matching score during dispatch (PRD §2.1, decisions Q14).
/// Completely deterministic, free of side effects, database calls, or I/O.
/// </summary>
public static class MatchingScoreCalculator
{
    private static readonly MatchingScoreOptions DefaultOptions = new();

    /// <summary>
    /// Computes the normalized matching score for a single candidate.
    /// </summary>
    /// <param name="candidate">Worker profile and distance to booking location.</param>
    /// <param name="searchRadiusKm">Current dispatch step radius in kilometers (e.g. 5.0, 7.0, 10.0).</param>
    /// <param name="options">Optional weighting and threshold options.</param>
    /// <returns>Deterministic <see cref="MatchingScoreResult"/>.</returns>
    public static MatchingScoreResult CalculateScore(
        DispatchCandidate candidate,
        double searchRadiusKm,
        MatchingScoreOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (searchRadiusKm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(searchRadiusKm), "Search radius must be strictly positive.");
        }

        var opt = options ?? DefaultOptions;

        // 1. Check distance eligibility
        if (candidate.DistanceKm < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(candidate.DistanceKm), "Distance cannot be negative.");
        }

        if (candidate.DistanceKm > searchRadiusKm)
        {
            return new MatchingScoreResult
            {
                WorkerId = candidate.WorkerId,
                TotalScore = 0.0,
                DistanceScore = 0.0,
                RatingScore = 0.0,
                HistoryScore = 0.0,
                IsEligible = false,
                IneligibilityReason = $"Distance ({candidate.DistanceKm:F2} km) exceeds search radius ({searchRadiusKm:F2} km)."
            };
        }

        // 2. Check rating threshold eligibility (decisions Q14: rating < 4.00 after >= 10 jobs)
        if (candidate.CompletedJobs >= opt.MinJobsForRatingFilter && candidate.RatingAvg < opt.MinRatingThreshold)
        {
            return new MatchingScoreResult
            {
                WorkerId = candidate.WorkerId,
                TotalScore = 0.0,
                DistanceScore = 0.0,
                RatingScore = 0.0,
                HistoryScore = 0.0,
                IsEligible = false,
                IneligibilityReason = $"Worker rating ({candidate.RatingAvg:F2}) is below minimum threshold ({opt.MinRatingThreshold:F2}) after {candidate.CompletedJobs} completed jobs (decisions Q14)."
            };
        }

        // 3. Normalized Distance Score: 1.0 at 0km, linearly decreasing to 0.0 at searchRadiusKm
        double distanceRatio = candidate.DistanceKm / searchRadiusKm;
        double distanceScore = Math.Clamp(1.0 - distanceRatio, 0.0, 1.0);

        // 4. Normalized Rating Score: scale 1.0 -> 0.0, 5.0 -> 1.0
        double clampedRating = (double)Math.Clamp(candidate.RatingAvg, 1.0m, 5.0m);
        double ratingScore = Math.Clamp((clampedRating - 1.0) / 4.0, 0.0, 1.0);

        // 5. Normalized Job History Score: experience factor up to benchmark
        double historyScore = opt.BenchmarkCompletedJobs > 0
            ? Math.Clamp((double)candidate.CompletedJobs / opt.BenchmarkCompletedJobs, 0.0, 1.0)
            : 1.0;

        // 6. Total weighted score
        double totalScore = (distanceScore * opt.DistanceWeight) +
                            (ratingScore * opt.RatingWeight) +
                            (historyScore * opt.HistoryWeight);

        return new MatchingScoreResult
        {
            WorkerId = candidate.WorkerId,
            TotalScore = Math.Round(totalScore, 4),
            DistanceScore = Math.Round(distanceScore, 4),
            RatingScore = Math.Round(ratingScore, 4),
            HistoryScore = Math.Round(historyScore, 4),
            IsEligible = true,
            IneligibilityReason = null
        };
    }

    /// <summary>
    /// Evaluates and ranks all candidates descending by total matching score.
    /// Ineligible candidates are filtered out.
    /// Tie-breaking order: TotalScore DESC -> DistanceKm ASC -> RatingAvg DESC -> CompletedJobs DESC.
    /// </summary>
    public static IReadOnlyList<(DispatchCandidate Candidate, MatchingScoreResult Result)> RankCandidates(
        IEnumerable<DispatchCandidate> candidates,
        double searchRadiusKm,
        MatchingScoreOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var opt = options ?? DefaultOptions;

        return candidates
            .Select(c => (Candidate: c, Result: CalculateScore(c, searchRadiusKm, opt)))
            .Where(x => x.Result.IsEligible)
            .OrderByDescending(x => x.Result.TotalScore)
            .ThenBy(x => x.Candidate.DistanceKm)
            .ThenByDescending(x => x.Candidate.RatingAvg)
            .ThenByDescending(x => x.Candidate.CompletedJobs)
            .ToList();
    }
}
