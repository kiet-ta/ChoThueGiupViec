using CommonService.Application.Common.Options;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Dispatch;

/// <summary>
/// Result of scanning and ranking available candidates for a dispatch radius step.
/// </summary>
public sealed record SteppedScanStepResult
{
    public required double RadiusKm { get; init; }
    public required int StepIndex { get; init; }
    public required IReadOnlyList<(DispatchCandidate Candidate, MatchingScoreResult ScoreResult)> RankedCandidates { get; init; }
    public bool HasCandidates => RankedCandidates.Count > 0;
}

/// <summary>
/// Overall result of the stepped radius scan (5 -> 7 -> 10 km).
/// </summary>
public sealed record SteppedScanResult
{
    public required ServiceTier ServiceTier { get; init; }
    public required IReadOnlyList<SteppedScanStepResult> StepsExecuted { get; init; }
    public (DispatchCandidate Candidate, MatchingScoreResult ScoreResult)? BestCandidate { get; init; }
    public double? MatchedRadiusKm { get; init; }
    public bool ExhaustedAllSteps { get; init; }
}

/// <summary>
/// Scans workers across stepped radius boundaries (5 -> 7 -> 10 km) adhering to:
/// 1. Economy tier STRICTLY accepts Freelancers, never Agency workers (PRD §2.1).
/// 2. Workers with rating_avg &lt; 4.00 after &gt;= 10 completed jobs are excluded (Decisions Q14).
/// 3. Candidates within the step radius are evaluated and ranked via <see cref="MatchingScoreCalculator"/>.
/// </summary>
public class SteppedRadiusDispatchScanner
{
    private readonly IWorkerAvailabilityQuery _workerAvailabilityQuery;
    private readonly IWorkerProfileQuery? _workerProfileQuery;
    private readonly BusinessRules _businessRules;
    private readonly MatchingScoreOptions _matchingScoreOptions;
    private readonly ILogger<SteppedRadiusDispatchScanner> _logger;

    public SteppedRadiusDispatchScanner(
        IWorkerAvailabilityQuery workerAvailabilityQuery,
        IOptions<BusinessRules> businessRules,
        IOptions<MatchingScoreOptions>? matchingScoreOptions = null,
        IWorkerProfileQuery? workerProfileQuery = null,
        ILogger<SteppedRadiusDispatchScanner>? logger = null)
    {
        _workerAvailabilityQuery = workerAvailabilityQuery ?? throw new ArgumentNullException(nameof(workerAvailabilityQuery));
        _businessRules = businessRules?.Value ?? new BusinessRules();
        _matchingScoreOptions = matchingScoreOptions?.Value ?? new MatchingScoreOptions();
        _workerProfileQuery = workerProfileQuery;
        _logger = logger ?? NullLogger<SteppedRadiusDispatchScanner>.Instance;
    }

    /// <summary>
    /// Scans candidate workers across stepped radius (5 -> 7 -> 10 km) for a given order location and shift.
    /// Returns the step-by-step evaluation and the top candidate if found.
    /// </summary>
    public async Task<SteppedScanResult> ScanCandidatesAsync(
        ServiceTier serviceTier,
        DateOnly date,
        string shiftCode,
        GeoPoint location,
        int candidateLimitPerStep = 50,
        CancellationToken cancellationToken = default)
    {
        // Guard: PRD §2.1 Economy tier is for Freelancers; Agency workers must never enter Economy
        // If an agency staff candidate appears in the query results, they are strictly rejected for Economy.
        var radiusSteps = _businessRules.Dispatch.RadiusStepsKm;
        if (radiusSteps == null || radiusSteps.Length == 0)
        {
            radiusSteps = [5.0, 7.0, 10.0];
        }

        var executedSteps = new List<SteppedScanStepResult>();
        (DispatchCandidate Candidate, MatchingScoreResult ScoreResult)? bestCandidate = null;
        double? matchedRadius = null;

        for (int i = 0; i < radiusSteps.Length; i++)
        {
            double currentRadius = radiusSteps[i];

            var query = new AvailabilityQuery(
                Date: date,
                ShiftCode: shiftCode,
                Location: location,
                RadiusKm: currentRadius,
                Limit: candidateLimitPerStep
            );

            var availableWorkers = await _workerAvailabilityQuery.FindAvailableFreelancersAsync(query, cancellationToken);

            // Fetch profile metrics (Rating, CompletedJobs) if worker profile query is supplied, otherwise default
            IReadOnlyDictionary<int, WorkerProfileSummary>? profiles = null;
            if (_workerProfileQuery != null && availableWorkers.Count > 0)
            {
                var ids = availableWorkers.Select(w => w.WorkerId).Distinct();
                profiles = await _workerProfileQuery.GetManyAsync(ids, cancellationToken);
            }

            var candidates = new List<DispatchCandidate>();
            foreach (var w in availableWorkers)
            {
                var profile = profiles != null && profiles.TryGetValue(w.WorkerId, out var p) ? p : null;
                decimal rating = profile?.RatingAvg ?? 5.0m;
                int completedJobs = profile?.CompletedJobs ?? 0;

                // Candidate worker model
                candidates.Add(new DispatchCandidate
                {
                    WorkerId = w.WorkerId,
                    WorkerType = "FREELANCER", // AvailabilityQuery returns freelancers
                    AgencyId = null,
                    DistanceKm = w.DistanceKm,
                    RatingAvg = rating,
                    CompletedJobs = completedJobs,
                });
            }

            // Enforce Economy filter: strictly Freelancers
            var filteredCandidates = FilterCandidatesByServiceTier(candidates, serviceTier);

            // Rank candidates using pure function MatchingScoreCalculator
            var ranked = MatchingScoreCalculator.RankCandidates(
                filteredCandidates,
                currentRadius,
                _matchingScoreOptions
            );

            var stepResult = new SteppedScanStepResult
            {
                RadiusKm = currentRadius,
                StepIndex = i,
                RankedCandidates = ranked
            };
            executedSteps.Add(stepResult);

            if (ranked.Count > 0)
            {
                bestCandidate = ranked[0];
                matchedRadius = currentRadius;
                _logger.LogInformation(
                    "Found {Count} candidates at radius {Radius} km for tier {Tier}. Best WorkerId={WorkerId} with score {Score}.",
                    ranked.Count, currentRadius, serviceTier, bestCandidate.Value.Candidate.WorkerId, bestCandidate.Value.ScoreResult.TotalScore);
                break;
            }

            _logger.LogInformation(
                "No eligible candidates at radius {Radius} km for tier {Tier}. Expanding to next radius step if available.",
                currentRadius, serviceTier);
        }

        return new SteppedScanResult
        {
            ServiceTier = serviceTier,
            StepsExecuted = executedSteps,
            BestCandidate = bestCandidate,
            MatchedRadiusKm = matchedRadius,
            ExhaustedAllSteps = bestCandidate == null && executedSteps.Count == radiusSteps.Length
        };
    }

    /// <summary>
    /// Enforces service tier constraints:
    /// - Economy: strictly FREELANCER, never AGENCY (PRD §2.1).
    /// </summary>
    public static IEnumerable<DispatchCandidate> FilterCandidatesByServiceTier(
        IEnumerable<DispatchCandidate> candidates,
        ServiceTier serviceTier)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        foreach (var c in candidates)
        {
            if (serviceTier == ServiceTier.Economy)
            {
                // Strict check: WorkerType must be FREELANCER, and AgencyId must be null
                if (c.WorkerType.Equals("AGENCY", StringComparison.OrdinalIgnoreCase) ||
                    c.WorkerType.Equals("AGENCY_STAFF", StringComparison.OrdinalIgnoreCase) ||
                    c.AgencyId != null)
                {
                    continue; // Discard Agency candidate for Economy tier
                }
            }

            yield return c;
        }
    }
}
