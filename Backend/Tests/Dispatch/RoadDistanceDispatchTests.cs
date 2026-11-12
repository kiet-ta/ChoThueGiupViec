using System.Net;
using System.Text;
using CommonService.Application.Common.Options;
using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

namespace CommonService.Tests.Dispatch;

/// <summary>DISPATCH-ROAD-01 (#807): candidates are ranked and radius-tested by road distance; routing failures fall back to straight line.</summary>
public class RoadDistanceDispatchTests
{
    private static readonly GeoPoint Order = new(10.762622, 106.660172);
    private static readonly BusinessRules Rules = new() { Dispatch = new() { RadiusStepsKm = [5.0, 10.0, 20.0, 30.0] } };

    private class StubRouting(Func<IReadOnlyList<(double Lat, double Lng)>, IReadOnlyList<double?>?> answer) : IRoutingService
    {
        public int Calls { get; private set; }

        public Task<RouteResult?> GetRouteAsync(double a, double b, double c, double d, CancellationToken ct = default) =>
            Task.FromResult<RouteResult?>(null);

        public Task<IReadOnlyList<double?>?> GetRoadDistancesAsync(
            double fromLat, double fromLng, IReadOnlyList<(double Lat, double Lng)> targets, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(answer(targets));
        }
    }

    private static AvailableWorker W(int id, double straightKm, double lat) => new(id, id, straightKm, lat, 106.7);

    private static SteppedRadiusDispatchScanner Scanner(FakeWorkerAvailabilityQuery q, IRoutingService? routing) =>
        new(q, MicrosoftOptions.Create(Rules), MicrosoftOptions.Create(new MatchingScoreOptions()),
            workerProfileQuery: null, logger: NullLogger<SteppedRadiusDispatchScanner>.Instance, routingService: routing);

    private static Task<SteppedScanResult> Scan(SteppedRadiusDispatchScanner s, IReadOnlyCollection<long>? excluded = null) =>
        s.ScanCandidatesAsync(ServiceTier.Economy, new DateOnly(2026, 11, 12), "SHIFT_MORNING", Order, excludedWorkerIds: excluded);

    [Fact]
    public async Task Closer_by_straight_line_but_farther_by_road_is_offered_after()
    {
        var q = new FakeWorkerAvailabilityQuery();
        q.Workers.Add(W(1, 2.0, 10.70)); // A: 2 km straight, 4.5 km by road (across a river)
        q.Workers.Add(W(2, 3.0, 10.71)); // B: 3 km straight, 3.2 km by road
        var routing = new StubRouting(t => t.Select(x => (double?)(x.Lat == 10.70 ? 4500.0 : 3200.0)).ToList());

        var result = await Scan(Scanner(q, routing));

        Assert.Equal([2L, 1L], result.StepsExecuted[0].RankedCandidates.Select(r => (long)r.Candidate.WorkerId).ToArray());
        Assert.Equal(3.2, result.StepsExecuted[0].RankedCandidates[0].Candidate.DistanceKm, 3);
        Assert.Equal(1, routing.Calls);
    }

    [Fact]
    public async Task Next_candidate_after_decline_follows_road_order()
    {
        var q = new FakeWorkerAvailabilityQuery();
        q.Workers.Add(W(1, 1.0, 10.70));
        q.Workers.Add(W(2, 2.0, 10.71));
        q.Workers.Add(W(3, 3.0, 10.72));
        var routing = new StubRouting(t => t.Select(x => (double?)(x.Lat == 10.70 ? 4000.0 : x.Lat == 10.71 ? 1000.0 : 2000.0)).ToList());
        var scanner = Scanner(q, routing);

        var first = await Scan(scanner);
        var second = await Scan(scanner, [first.BestCandidate!.Value.Candidate.WorkerId]);
        var third = await Scan(scanner, [2, 3]);

        Assert.Equal(2, first.BestCandidate!.Value.Candidate.WorkerId);
        Assert.Equal(3, second.BestCandidate!.Value.Candidate.WorkerId);
        Assert.Equal(1, third.BestCandidate!.Value.Candidate.WorkerId);
    }

    [Fact]
    public async Task Worker_beyond_step_radius_by_road_is_skipped_then_found_at_next_step()
    {
        var q = new FakeWorkerAvailabilityQuery();
        q.Workers.Add(W(1, 4.0, 10.70)); // 4 km straight, 7 km by road
        var scanner = Scanner(q, new StubRouting(t => [7000.0]));

        var result = await Scan(scanner);

        Assert.Equal(2, result.StepsExecuted.Count);
        Assert.False(result.StepsExecuted[0].HasCandidates);
        Assert.Equal(10.0, result.MatchedRadiusKm);
        Assert.Equal(1, result.BestCandidate!.Value.Candidate.WorkerId);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("throws")]
    [InlineData("noroute")]
    public async Task Routing_failure_falls_back_to_straight_line_and_still_offers(string mode)
    {
        var q = new FakeWorkerAvailabilityQuery();
        q.Workers.Add(W(1, 3.0, 10.70));
        q.Workers.Add(W(2, 1.0, 10.71));
        IRoutingService routing = mode switch
        {
            "null" => new StubRouting(_ => null),
            "noroute" => new StubRouting(t => t.Select(_ => (double?)null).ToList()),
            _ => new StubRouting(_ => throw new HttpRequestException("down")),
        };

        var result = await Scan(Scanner(q, routing));

        Assert.Equal([2L, 1L], result.StepsExecuted[0].RankedCandidates.Select(r => (long)r.Candidate.WorkerId).ToArray());
        Assert.Equal(1.0, result.BestCandidate!.Value.Candidate.DistanceKm, 3);
    }

    [Fact]
    public async Task Without_routing_service_distance_is_unchanged()
    {
        var q = new FakeWorkerAvailabilityQuery();
        q.Workers.Add(W(1, 3.0, 10.70));

        var result = await Scan(Scanner(q, null));

        Assert.Equal(3.0, result.BestCandidate!.Value.Candidate.DistanceKm, 3);
    }

    [Fact]
    public async Task Preferred_worker_is_measured_by_road()
    {
        var q = new FakeWorkerAvailabilityQuery();
        q.Workers.Add(W(1, 4.0, 10.70));
        var scanner = Scanner(q, new StubRouting(_ => [6000.0]));

        var found = await scanner.FindWorkerCandidateAsync(ServiceTier.Economy, new DateOnly(2026, 11, 12), "SHIFT_MORNING", Order, 1);

        Assert.NotNull(found);
        Assert.Equal(6.0, found.Value.Candidate.DistanceKm, 3);
        Assert.Equal(10.0, found.Value.RadiusKm);
    }

    private class StubHandler(HttpStatusCode code, string body) : HttpMessageHandler
    {
        public string? LastUrl { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUrl = request.RequestUri!.ToString();
            LastBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static ValhallaRoutingService Valhalla(HttpMessageHandler h, string? baseUrl = "http://valhalla:8002") =>
        new(new HttpClient(h),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Valhalla:BaseUrl"] = baseUrl }).Build(),
            NullLogger<ValhallaRoutingService>.Instance);

    [Fact]
    public async Task Valhalla_matrix_is_one_call_and_unreachable_targets_are_null()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"sources_to_targets":[[{"distance":1.234,"to_index":0},{"distance":null,"to_index":1},{"distance":0.5,"to_index":2}]]}""");

        var meters = await Valhalla(handler).GetRoadDistancesAsync(10.7, 106.6, [(10.1, 106.1), (10.2, 106.2), (10.3, 106.3)]);

        Assert.Equal("http://valhalla:8002/sources_to_targets", handler.LastUrl);
        Assert.Contains("motor_scooter", handler.LastBody);
        Assert.Equal(new double?[] { 1234.0, null, 500.0 }, meters!.ToArray());
    }

    [Fact]
    public async Task Valhalla_matrix_http_error_returns_null()
    {
        var meters = await Valhalla(new StubHandler(HttpStatusCode.InternalServerError, "{}"))
            .GetRoadDistancesAsync(10.7, 106.6, [(10.1, 106.1)]);

        Assert.Null(meters);
    }

    [Fact]
    public async Task Valhalla_matrix_without_base_url_uses_straight_line()
    {
        var meters = await Valhalla(new StubHandler(HttpStatusCode.OK, "{}"), baseUrl: null)
            .GetRoadDistancesAsync(10.7, 106.6, [(10.7, 106.7)]);

        Assert.NotNull(meters![0]);
        Assert.InRange(meters[0]!.Value, 10_000, 12_000);
    }
}
