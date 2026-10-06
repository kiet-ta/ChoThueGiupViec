using CommonService.Application.Common.Options;
using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

namespace CommonService.Tests.Dispatch;

public class SteppedRadiusDispatchScannerTests
{
    private readonly BusinessRules _businessRules = new();
    private readonly MatchingScoreOptions _matchingScoreOptions = new();

    [Fact]
    public async Task Scan_finds_worker_at_first_step_5km()
    {
        var fakeQuery = new FakeWorkerAvailabilityQuery();
        fakeQuery.Workers.Add(new AvailableWorker(WorkerId: 101, SlotId: 1, DistanceKm: 3.2));
        fakeQuery.Workers.Add(new AvailableWorker(WorkerId: 102, SlotId: 2, DistanceKm: 6.5));

        var scanner = new SteppedRadiusDispatchScanner(
            fakeQuery,
            MicrosoftOptions.Create(_businessRules),
            MicrosoftOptions.Create(_matchingScoreOptions)
        );

        var result = await scanner.ScanCandidatesAsync(
            ServiceTier.Economy,
            new DateOnly(2026, 10, 10),
            "SANG",
            new GeoPoint(10.762622, 106.660172)
        );

        Assert.NotNull(result.BestCandidate);
        Assert.Equal(101, result.BestCandidate.Value.Candidate.WorkerId);
        Assert.Equal(5.0, result.MatchedRadiusKm);
        Assert.Single(result.StepsExecuted);
        Assert.False(result.ExhaustedAllSteps);
    }

    [Fact]
    public async Task Scan_progresses_from_5km_to_7km_when_no_worker_in_5km()
    {
        var fakeQuery = new FakeWorkerAvailabilityQuery();
        // Worker is at 6.2 km (outside 5 km, inside 7 km)
        fakeQuery.Workers.Add(new AvailableWorker(WorkerId: 201, SlotId: 1, DistanceKm: 6.2));

        var scanner = new SteppedRadiusDispatchScanner(
            fakeQuery,
            MicrosoftOptions.Create(_businessRules),
            MicrosoftOptions.Create(_matchingScoreOptions)
        );

        var result = await scanner.ScanCandidatesAsync(
            ServiceTier.Economy,
            new DateOnly(2026, 10, 10),
            "CHIEU",
            new GeoPoint(10.762622, 106.660172)
        );

        Assert.NotNull(result.BestCandidate);
        Assert.Equal(201, result.BestCandidate.Value.Candidate.WorkerId);
        Assert.Equal(7.0, result.MatchedRadiusKm);
        Assert.Equal(2, result.StepsExecuted.Count);
        Assert.Empty(result.StepsExecuted[0].RankedCandidates);
        Assert.Single(result.StepsExecuted[1].RankedCandidates);
        Assert.False(result.ExhaustedAllSteps);
    }

    [Fact]
    public async Task Scan_progresses_to_10km_when_no_worker_in_5km_and_7km()
    {
        var fakeQuery = new FakeWorkerAvailabilityQuery();
        // Worker is at 8.5 km (outside 7 km, inside 10 km)
        fakeQuery.Workers.Add(new AvailableWorker(WorkerId: 301, SlotId: 1, DistanceKm: 8.5));

        var scanner = new SteppedRadiusDispatchScanner(
            fakeQuery,
            MicrosoftOptions.Create(_businessRules),
            MicrosoftOptions.Create(_matchingScoreOptions)
        );

        var result = await scanner.ScanCandidatesAsync(
            ServiceTier.Economy,
            new DateOnly(2026, 10, 10),
            "TOI",
            new GeoPoint(10.762622, 106.660172)
        );

        Assert.NotNull(result.BestCandidate);
        Assert.Equal(301, result.BestCandidate.Value.Candidate.WorkerId);
        Assert.Equal(10.0, result.MatchedRadiusKm);
        Assert.Equal(3, result.StepsExecuted.Count);
        Assert.Empty(result.StepsExecuted[0].RankedCandidates);
        Assert.Empty(result.StepsExecuted[1].RankedCandidates);
        Assert.Single(result.StepsExecuted[2].RankedCandidates);
        Assert.False(result.ExhaustedAllSteps);
    }

    [Fact]
    public async Task Scan_exhausts_all_steps_when_no_worker_within_10km()
    {
        var fakeQuery = new FakeWorkerAvailabilityQuery();
        // Worker is at 12.0 km (outside 10 km)
        fakeQuery.Workers.Add(new AvailableWorker(WorkerId: 401, SlotId: 1, DistanceKm: 12.0));

        var scanner = new SteppedRadiusDispatchScanner(
            fakeQuery,
            MicrosoftOptions.Create(_businessRules),
            MicrosoftOptions.Create(_matchingScoreOptions)
        );

        var result = await scanner.ScanCandidatesAsync(
            ServiceTier.Economy,
            new DateOnly(2026, 10, 10),
            "SANG",
            new GeoPoint(10.762622, 106.660172)
        );

        Assert.Null(result.BestCandidate);
        Assert.Null(result.MatchedRadiusKm);
        Assert.Equal(3, result.StepsExecuted.Count);
        Assert.True(result.ExhaustedAllSteps);
    }

    [Fact]
    public void FilterCandidatesByServiceTier_strictly_excludes_Agency_from_Economy()
    {
        var candidates = new List<DispatchCandidate>
        {
            new() { WorkerId = 1, WorkerType = "FREELANCER", DistanceKm = 2.0, RatingAvg = 4.8m, CompletedJobs = 20 },
            new() { WorkerId = 2, WorkerType = "AGENCY", AgencyId = 5, DistanceKm = 1.0, RatingAvg = 5.0m, CompletedJobs = 30 },
            new() { WorkerId = 3, WorkerType = "AGENCY_STAFF", AgencyId = 8, DistanceKm = 1.5, RatingAvg = 4.9m, CompletedJobs = 25 },
            new() { WorkerId = 4, WorkerType = "FREELANCER", DistanceKm = 3.0, RatingAvg = 4.7m, CompletedJobs = 15 }
        };

        var filtered = SteppedRadiusDispatchScanner.FilterCandidatesByServiceTier(candidates, ServiceTier.Economy).ToList();

        Assert.Equal(2, filtered.Count);
        Assert.Contains(filtered, c => c.WorkerId == 1 && c.WorkerType == "FREELANCER");
        Assert.Contains(filtered, c => c.WorkerId == 4 && c.WorkerType == "FREELANCER");
        Assert.DoesNotContain(filtered, c => c.WorkerId == 2);
        Assert.DoesNotContain(filtered, c => c.WorkerId == 3);
    }

    [Fact]
    public async Task Scan_excludes_workers_with_rating_under_4_after_10_completed_jobs_Q14()
    {
        var fakeQuery = new FakeWorkerAvailabilityQuery();
        fakeQuery.Workers.Add(new AvailableWorker(WorkerId: 501, SlotId: 1, DistanceKm: 2.0));
        fakeQuery.Workers.Add(new AvailableWorker(WorkerId: 502, SlotId: 2, DistanceKm: 3.0));

        var fakeProfileQuery = new FakeWorkerProfileQuery();
        fakeProfileQuery.Add(new WorkerProfileSummary(
            WorkerId: 501,
            FullName: "Low Rating Worker",
            RatingAvg: 3.8m,
            CompletedJobs: 15, // >= 10 jobs and rating < 4.00 -> Ineligible (Q14)
            WorkStatus: WorkStatus.Idle
        ));
        fakeProfileQuery.Add(new WorkerProfileSummary(
            WorkerId: 502,
            FullName: "Eligible Worker",
            RatingAvg: 4.5m,
            CompletedJobs: 20,
            WorkStatus: WorkStatus.Idle
        ));

        var scanner = new SteppedRadiusDispatchScanner(
            fakeQuery,
            MicrosoftOptions.Create(_businessRules),
            MicrosoftOptions.Create(_matchingScoreOptions),
            workerProfileQuery: fakeProfileQuery
        );

        var result = await scanner.ScanCandidatesAsync(
            ServiceTier.Economy,
            new DateOnly(2026, 10, 10),
            "SANG",
            new GeoPoint(10.762622, 106.660172)
        );

        Assert.NotNull(result.BestCandidate);
        Assert.Equal(502, result.BestCandidate.Value.Candidate.WorkerId);
        // Step has only 1 eligible candidate since 501 is filtered out by Q14
        Assert.Single(result.StepsExecuted[0].RankedCandidates);
    }
}
