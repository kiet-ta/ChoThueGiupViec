using System.Net;
using System.Text;
using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Dispatch;
using CommonService.Application.Features.Workers;
using CommonService.Application.Features.Workers.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Services;
using CommonService.WebAPI.Controllers.Workers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CommonService.Tests.Workers;

/// <summary>ROUTE-MOB-01 (#760): <c>GET /api/v1/workers/assignments/{id}/route</c> gives the worker up to 3 ways to the customer's house.</summary>
public class WorkerRouteTests
{
    private const long AssignmentId = 55;
    private const int WorkerId = 7;

    private class TestCurrentUser(int? id) : ICurrentUser
    {
        public int? UserId { get; } = id;
        public UserRole? Role { get; } = UserRole.Worker;
        public bool IsAuthenticated => UserId.HasValue;
    }

    private class StubRepository(int ownerWorkerId, WorkerAssignmentDetailRow? row) : IWorkerAssignmentRepository
    {
        public Task<IReadOnlyList<WorkerAssignmentRow>> GetForWorkerAsync(
            int workerId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkerAssignmentRow>>([]);

        public Task<WorkerAssignmentDetailRow?> FindDetailForWorkerAsync(
            long assignmentId, int workerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(row is not null && row.AssignmentId == assignmentId && workerId == ownerWorkerId ? row : null);
    }

    private sealed class Router(params RouteResult[] routes) : IRoutingService
    {
        public (double, double, double, double, int)? Called { get; private set; }

        public Task<RouteResult?> GetRouteAsync(double fromLat, double fromLng, double toLat, double toLng, CancellationToken ct = default) =>
            Task.FromResult(routes.Length == 0 ? null : routes[0]);

        public Task<IReadOnlyList<RouteResult>> GetRoutesAsync(
            double fromLat, double fromLng, double toLat, double toLng, int maxRoutes, CancellationToken ct = default)
        {
            Called = (fromLat, fromLng, toLat, toLng, maxRoutes);
            return Task.FromResult<IReadOnlyList<RouteResult>>(routes);
        }
    }

    private static WorkerAssignmentDetailRow Row(JobAssignmentStatus status = JobAssignmentStatus.Assigned) =>
        new(AssignmentId, 155, "GV-55", new DateOnly(2026, 11, 5), "SHIFT_MORNING", new TimeOnly(8, 0), new TimeOnly(12, 0), status,
            ServiceTier.Premium, "12 Nguyễn Huệ", "Quận 1", "TP. Hồ Chí Minh", 10.803100m, 106.714700m, 128000m, null, null, null);

    private static WorkerRouteService Service(WorkerAssignmentDetailRow? row, Router router, int? workerId = WorkerId, int owner = WorkerId) =>
        new(new StubRepository(owner, row), router, new TestCurrentUser(workerId));

    private static readonly RouteResult Fast = new(5255, 805.8, "fast");
    private static readonly RouteResult Mid = new(5882, 845.8, "mid");
    private static readonly RouteResult Slow = new(6442, 991.8, "slow");

    [Fact]
    public async Task OwnAcceptedJob_ReturnsAllRoutesAndTheDestinationOfTheOrder()
    {
        var router = new Router(Fast, Mid, Slow);

        var result = await Service(Row(), router).GetAsync(AssignmentId, 10.7769, 106.7009);

        Assert.NotNull(result);
        Assert.Equal(["fast", "mid", "slow"], result!.Routes.Select(r => r.Polyline));
        Assert.Equal(5255, result.Routes[0].DistanceMeters);
        Assert.Equal(10.8031, result.DestinationLatitude);
        Assert.Equal(106.7147, result.DestinationLongitude);
        Assert.Equal((10.7769, 106.7009, 10.8031, 106.7147, 3), router.Called);
    }

    [Fact]
    public async Task OneRoute_IsStillAList()
    {
        var result = await Service(Row(), new Router(Fast)).GetAsync(AssignmentId, 10.7769, 106.7009);
        Assert.Single(result!.Routes);
    }

    [Fact]
    public async Task CheckedInJob_StillHasARoute()
    {
        Assert.NotNull(await Service(Row(JobAssignmentStatus.CheckedIn), new Router(Fast)).GetAsync(AssignmentId, 10.7769, 106.7009));
    }

    [Theory]
    [InlineData(JobAssignmentStatus.InProgress)]
    [InlineData(JobAssignmentStatus.AwaitingAcceptance)]
    [InlineData(JobAssignmentStatus.Completed)]
    public async Task JobThatAlreadyStarted_Is409(JobAssignmentStatus status)
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => Service(Row(status), new Router(Fast)).GetAsync(AssignmentId, 10.7769, 106.7009));
        Assert.Equal(WorkerRouteErrorCodes.NotOnTheWay, ex.Code);
    }

    [Theory]
    [InlineData(JobAssignmentStatus.Offered)]
    [InlineData(JobAssignmentStatus.Cancelled)]
    public async Task AnOfferOrAnEndedJob_IsNotFound(JobAssignmentStatus status)
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => Service(Row(status), new Router(Fast)).GetAsync(AssignmentId, 10.7769, 106.7009));
    }

    [Fact]
    public async Task AnotherWorkersJob_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => Service(Row(), new Router(Fast), workerId: 8, owner: WorkerId).GetAsync(AssignmentId, 10.7769, 106.7009));
    }

    [Theory]
    [InlineData(91, 106)]
    [InlineData(10, 181)]
    [InlineData(double.NaN, 106)]
    public async Task OutOfRangePosition_IsAValidationError(double lat, double lng)
    {
        await Assert.ThrowsAsync<ValidationException>(() => Service(Row(), new Router(Fast)).GetAsync(AssignmentId, lat, lng));
    }

    [Fact]
    public async Task RoutingEngineDown_GivesNull_And_TheEndpointAnswers502()
    {
        var service = Service(Row(), new Router());
        Assert.Null(await service.GetAsync(AssignmentId, 10.7769, 106.7009));

        var controller = BuildController(service);
        var res = await controller.GetAssignmentRoute(AssignmentId, 10.7769, 106.7009, service, CancellationToken.None);
        Assert.Equal(502, Assert.IsType<ObjectResult>(res).StatusCode);
    }

    [Fact]
    public async Task Endpoint_Returns200WithTheRoutes()
    {
        var service = Service(Row(), new Router(Fast, Mid));
        var res = await BuildController(service).GetAssignmentRoute(AssignmentId, 10.7769, 106.7009, service, CancellationToken.None);

        var body = Assert.IsType<ApiResponse<WorkerRouteResponse>>(Assert.IsType<OkObjectResult>(res).Value);
        Assert.Equal(2, body.Data!.Routes.Count);
    }

    private static WorkersController BuildController(WorkerRouteService service) =>
        new(null!, null!, null!, null!, null!, null!, null!, null!, null!);

    // ---- Valhalla alternates ----

    private sealed class Handler(string body) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static ValhallaRoutingService Valhalla(Handler handler, string? baseUrl = "http://localhost:8002") =>
        new(new HttpClient(handler),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Valhalla:BaseUrl"] = baseUrl }).Build(),
            NullLogger<ValhallaRoutingService>.Instance);

    private const string ThreeRoutes = """
        {"trip":{"summary":{"length":5.882,"time":845.8},"legs":[{"shape":"mid"}]},
         "alternates":[{"trip":{"summary":{"length":6.442,"time":991.8},"legs":[{"shape":"slow"}]}},
                       {"trip":{"summary":{"length":5.255,"time":805.8},"legs":[{"shape":"fast"}]}}]}
        """;

    [Fact]
    public async Task Valhalla_AsksForAlternates_And_SortsByDuration()
    {
        var handler = new Handler(ThreeRoutes);

        var routes = await Valhalla(handler).GetRoutesAsync(10.7769, 106.7009, 10.8031, 106.7147, 3);

        Assert.Equal(["fast", "mid", "slow"], routes.Select(r => r.Polyline));
        Assert.Equal(5255.0, routes[0].DistanceMeters);
        Assert.Contains("\"alternates\":2", handler.Body);
        Assert.Contains("motor_scooter", handler.Body);
    }

    [Fact]
    public async Task Valhalla_RespectsTheMaximum()
    {
        var routes = await Valhalla(new Handler(ThreeRoutes)).GetRoutesAsync(10.7769, 106.7009, 10.8031, 106.7147, 2);
        Assert.Equal(["fast", "mid"], routes.Select(r => r.Polyline));
    }

    [Fact]
    public async Task Valhalla_WithoutAlternates_GivesTheSingleRoute()
    {
        const string json = """{"trip":{"summary":{"length":3.2,"time":780.4},"legs":[{"shape":"only"}]}}""";
        var routes = await Valhalla(new Handler(json)).GetRoutesAsync(10.7769, 106.7009, 10.8031, 106.7147, 3);
        Assert.Single(routes);
    }

    [Fact]
    public async Task NoBaseUrl_UsesTheFake_WithOneRoute()
    {
        var routes = await Valhalla(new Handler("{}"), baseUrl: "").GetRoutesAsync(10.7769, 106.7009, 10.8031, 106.7147, 3);
        Assert.Single(routes);
    }

    [Fact]
    public void ParseRoutes_OfNothing_IsEmpty()
    {
        Assert.Empty(ValhallaRoutingService.ParseRoutes(null, 3));
    }
}
