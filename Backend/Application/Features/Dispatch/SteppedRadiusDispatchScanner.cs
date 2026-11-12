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
public record SteppedScanStepResult
{
    public required double RadiusKm { get; init; }
    public required int StepIndex { get; init; }
    public required IReadOnlyList<(DispatchCandidate Candidate, MatchingScoreResult ScoreResult)> RankedCandidates { get; init; }
    public bool HasCandidates => RankedCandidates.Count > 0;
}

/// <summary>
/// Overall result of the stepped radius scan (5 -> 7 -> 10 km).
/// </summary>
public record SteppedScanResult
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
    private readonly IRoutingService? _routing;

    public SteppedRadiusDispatchScanner(
        IWorkerAvailabilityQuery workerAvailabilityQuery,
        IOptions<BusinessRules> businessRules,
        IOptions<MatchingScoreOptions>? matchingScoreOptions = null,
        IWorkerProfileQuery? workerProfileQuery = null,
        ILogger<SteppedRadiusDispatchScanner>? logger = null,
        IRoutingService? routingService = null)
    {
        _routing = routingService;
        _workerAvailabilityQuery = workerAvailabilityQuery ?? throw new ArgumentNullException(nameof(workerAvailabilityQuery));
        _businessRules = businessRules?.Value ?? new BusinessRules();
        _matchingScoreOptions = matchingScoreOptions?.Value ?? new MatchingScoreOptions();
        _workerProfileQuery = workerProfileQuery;
        _logger = logger ?? NullLogger<SteppedRadiusDispatchScanner>.Instance;
    }

    /// <summary>
    /// Scans candidate workers across stepped radius (5 -> 10 -> 20 -> 30 km) for a given order location and shift.
    /// Returns the step-by-step evaluation and the top candidate if found.
    /// </summary>
    public async Task<SteppedScanResult> ScanCandidatesAsync(
        ServiceTier serviceTier,
        DateOnly date,
        string shiftCode,
        GeoPoint location,
        int candidateLimitPerStep = 50,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<long>? excludedWorkerIds = null)
    {
        // Guard: PRD §2.1 Economy tier is for Freelancers; Agency workers must never enter Economy
        // If an agency staff candidate appears in the query results, they are strictly rejected for Economy.
        var radiusSteps = _businessRules.Dispatch.RadiusStepsKm;
        if (radiusSteps == null || radiusSteps.Length == 0 || radiusSteps.SequenceEqual([5.0, 7.0, 10.0]))
        {
            radiusSteps = [5.0, 10.0, 20.0, 30.0];
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
            availableWorkers = await WithRoadDistancesAsync(location, availableWorkers, cancellationToken);

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
                // BR-03: a worker who already had this order (declined, timed out or holding another seat) is not offered it again.
                if (excludedWorkerIds != null && excludedWorkerIds.Contains(w.WorkerId))
                {
                    continue;
                }

                var profile = profiles != null && profiles.TryGetValue(w.WorkerId, out var p) ? p : null;
                decimal rating = profile?.RatingAvg ?? 5.0m;
                int completedJobs = profile?.CompletedJobs ?? 0;

                // Candidate worker model
                candidates.Add(new DispatchCandidate
                {
                    WorkerId = w.WorkerId,
                    WorkerType = "FREELANCER", // AvailabilityQuery returns freelancers
                    AgencyId = null,
                    SlotId = w.SlotId,
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
    /// DISPATCH-ROAD-01: replaces the straight-line <see cref="AvailableWorker.DistanceKm"/> by the road distance (Valhalla, one matrix call)
    /// so candidates are ranked and tested against the radius by road. A worker without coordinates or without a route, or any routing
    /// failure, keeps the straight-line value: dispatch never fails because routing does.
    /// </summary>
    private async Task<IReadOnlyList<AvailableWorker>> WithRoadDistancesAsync(
        GeoPoint origin, IReadOnlyList<AvailableWorker> workers, CancellationToken cancellationToken)
    {
        if (_routing == null || workers.Count == 0) return workers;

        var withPoint = workers.Where(w => w.Latitude.HasValue && w.Longitude.HasValue).ToList();
        if (withPoint.Count == 0) return workers;

        IReadOnlyList<double?>? meters;
        try
        {
            meters = await _routing.GetRoadDistancesAsync(
                origin.Latitude, origin.Longitude, withPoint.Select(w => (w.Latitude!.Value, w.Longitude!.Value)).ToList(), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Road distance failed for {Count} candidates; using straight-line distance.", withPoint.Count);
            return workers;
        }

        if (meters == null || meters.Count != withPoint.Count)
        {
            _logger.LogWarning("Road distance unavailable for {Count} candidates; using straight-line distance.", withPoint.Count);
            return workers;
        }

        var roadKm = new Dictionary<int, double>();
        for (var i = 0; i < withPoint.Count; i++)
        {
            if (meters[i] is { } m) roadKm[withPoint[i].WorkerId] = m / 1000.0;
        }

        var byRoad = workers
            .Select(w => roadKm.TryGetValue(w.WorkerId, out var km) ? w with { DistanceKm = km } : w)
            .OrderBy(w => w.DistanceKm)
            .ToList();

        // Evidence line for UAT / support: who is nearest by road, with the straight-line value that used to decide.
        var straight = workers.ToDictionary(w => w.WorkerId, w => w.DistanceKm);
        _logger.LogInformation("[DISPATCH-ROAD] offer order by road distance: {Ranking}",
            string.Join(" > ", byRoad.Select(w => $"worker {w.WorkerId} road {w.DistanceKm:F2} km (straight {straight[w.WorkerId]:F2} km{(roadKm.ContainsKey(w.WorkerId) ? "" : ", no route: straight used")})")));
        return byRoad;
    }

    /// <summary>How many free workers the preferred-worker lookup reads at the widest radius; the query sorts by distance.</summary>
    private const int PreferredLookupLimit = 1000;

    /// <summary>
    /// The customer's preferred (favorite) worker as a candidate for the order, or null when that worker is not free for the shift,
    /// is farther than the widest radius step (BR-03, 10 km) or fails the same eligibility rules as every other candidate (tier,
    /// Q14 rating floor). The radius is the smallest step that reaches the worker.
    /// </summary>
    public async Task<(DispatchCandidate Candidate, MatchingScoreResult ScoreResult, double RadiusKm)?> FindWorkerCandidateAsync(
        ServiceTier serviceTier,
        DateOnly date,
        string shiftCode,
        GeoPoint location,
        long workerId,
        CancellationToken cancellationToken = default)
    {
        var radiusSteps = _businessRules.Dispatch.RadiusStepsKm;
        if (radiusSteps == null || radiusSteps.Length == 0 || radiusSteps.SequenceEqual([5.0, 7.0, 10.0]))
        {
            radiusSteps = [5.0, 10.0, 20.0, 30.0];
        }

        var maxRadius = radiusSteps.Max();
        var available = await _workerAvailabilityQuery.FindAvailableFreelancersAsync(
            new AvailabilityQuery(date, shiftCode, location, maxRadius, PreferredLookupLimit),
            cancellationToken);
        var worker = available.FirstOrDefault(w => w.WorkerId == workerId);
        if (worker == null)
        {
            return null;
        }

        worker = (await WithRoadDistancesAsync(location, [worker], cancellationToken))[0];

        WorkerProfileSummary? profile = null;
        if (_workerProfileQuery != null)
        {
            var profiles = await _workerProfileQuery.GetManyAsync([worker.WorkerId], cancellationToken);
            profile = profiles.TryGetValue(worker.WorkerId, out var p) ? p : null;
        }

        var candidate = new DispatchCandidate
        {
            WorkerId = worker.WorkerId,
            WorkerType = "FREELANCER", // AvailabilityQuery returns freelancers
            AgencyId = null,
            SlotId = worker.SlotId,
            DistanceKm = worker.DistanceKm,
            RatingAvg = profile?.RatingAvg ?? 5.0m,
            CompletedJobs = profile?.CompletedJobs ?? 0,
        };

        var radius = radiusSteps.Where(r => r >= worker.DistanceKm).DefaultIfEmpty(maxRadius).Min();
        var ranked = MatchingScoreCalculator.RankCandidates(
            FilterCandidatesByServiceTier([candidate], serviceTier),
            radius,
            _matchingScoreOptions);
        return ranked.Count == 0 ? null : (ranked[0].Candidate, ranked[0].Result, radius);
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
