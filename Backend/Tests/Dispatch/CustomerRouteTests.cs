using System.Net;
using System.Text;
using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Dispatch;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Services;
using CommonService.WebAPI.Controllers.Dispatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Tests.Dispatch;

/// <summary>ROUTE-WEB-01 (#765): the customer sees the route from the worker to the house (Valhalla, motor_scooter).</summary>
public class CustomerRouteTests
{
    private const int CustomerId = 11;
    private static readonly GeoPoint House = new(10.803100, 106.714700);

    private sealed class Query : IWorkerDispatchQuery
    {
        public OfferView? View { get; set; }

        public Task<OfferView?> GetOfferViewAsync(long assignmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(View);

        public Task<GeoPoint?> GetOrderLocationOfAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<GeoPoint?>(View?.OrderLocation);
    }

    private sealed class Router(RouteResult? result) : IRoutingService
    {
        public (double FromLat, double FromLng, double ToLat, double ToLng)? Called { get; private set; }

        public Task<RouteResult?> GetRouteAsync(double fromLat, double fromLng, double toLat, double toLng, CancellationToken ct = default)
        {
            Called = (fromLat, fromLng, toLat, toLng);
            return Task.FromResult(result);
        }
    }

    private sealed class User(int? id, UserRole role) : ICurrentUser
    {
        public bool IsAuthenticated => id != null;
        public int? UserId => id;
        public UserRole? Role => role;
    }

    private static OfferView View(JobAssignmentStatus status, GeoPoint? worker, int customerId = CustomerId)
    {
        var a = new JobAssignment { AssignmentId = 500, OrderId = 50, CustomerId = customerId, WorkerId = 21 };
        a.TransitionTo(JobAssignmentStatus.Assigned);
        if (status != JobAssignmentStatus.Assigned) a.TransitionTo(status);
        return new OfferView(a, new DateOnly(2026, 11, 12), new TimeOnly(8, 0), new TimeOnly(12, 0), "Bình Thạnh", House, worker);
    }

    private static CustomerRouteService Service(Query q, Router r, int customerId = CustomerId) =>
        new(q, r, new User(customerId, UserRole.Customer));

    private static readonly RouteResult Sample = new(3200.4, 780.0, "abc");

    [Fact]
    public async Task Own_assigned_job_returns_route_with_rounded_worker_position()
    {
        var q = new Query { View = View(JobAssignmentStatus.Assigned, new GeoPoint(10.776912, 106.700934)) };
        var router = new Router(Sample);

        var dto = await Service(q, router).GetAsync(500);

        Assert.NotNull(dto);
        Assert.Equal(3200.4, dto!.DistanceMeters);
        Assert.Equal(780.0, dto.DurationSeconds);
        Assert.Equal("abc", dto.Polyline);
        Assert.Equal(10.777, dto.WorkerPosition.Latitude);
        Assert.Equal(106.701, dto.WorkerPosition.Longitude);
        Assert.Equal(House.Latitude, dto.Destination.Latitude);
        // the route is computed from the rounded position, never from the exact one
        Assert.Equal((10.777, 106.701, House.Latitude, House.Longitude), router.Called);
    }

    [Fact]
    public async Task Another_customers_assignment_is_not_found()
    {
        var q = new Query { View = View(JobAssignmentStatus.Assigned, new GeoPoint(10.77, 106.70), customerId: 99) };
        await Assert.ThrowsAsync<NotFoundException>(() => Service(q, new Router(Sample)).GetAsync(500));
    }

    [Fact]
    public async Task Unknown_assignment_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Service(new Query(), new Router(Sample)).GetAsync(500));
    }

    [Fact]
    public async Task Job_that_is_not_on_the_way_is_a_409()
    {
        var q = new Query { View = View(JobAssignmentStatus.CheckedIn, new GeoPoint(10.77, 106.70)) };
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => Service(q, new Router(Sample)).GetAsync(500));
        Assert.Equal(RouteErrorCodes.NotOnTheWay, ex.Code);
    }

    [Fact]
    public async Task Worker_without_a_position_is_a_409()
    {
        var q = new Query { View = View(JobAssignmentStatus.Assigned, null) };
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => Service(q, new Router(Sample)).GetAsync(500));
        Assert.Equal(RouteErrorCodes.WorkerLocationUnknown, ex.Code);
    }

    [Fact]
    public async Task Routing_engine_failure_gives_null()
    {
        var q = new Query { View = View(JobAssignmentStatus.Assigned, new GeoPoint(10.77, 106.70)) };
        Assert.Null(await Service(q, new Router(null)).GetAsync(500));
    }

    [Fact]
    public async Task Controller_maps_null_to_502_and_a_route_to_200()
    {
        var q = new Query { View = View(JobAssignmentStatus.Assigned, new GeoPoint(10.77, 106.70)) };

        var bad = await new CustomerRouteController(Service(q, new Router(null))).Get(500, CancellationToken.None);
        Assert.Equal(502, Assert.IsType<ObjectResult>(bad).StatusCode);

        var ok = await new CustomerRouteController(Service(q, new Router(Sample))).Get(500, CancellationToken.None);
        var envelope = Assert.IsType<ApiResponse<CustomerRouteDto>>(Assert.IsType<OkObjectResult>(ok).Value);
        Assert.Equal(500, envelope.Data!.AssignmentId);
    }

    // ---- ValhallaRoutingService ----

    private sealed class Handler(HttpStatusCode status, string body, bool throws = false) : HttpMessageHandler
    {
        public string? Url { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (throws) throw new HttpRequestException("down");
            Url = request.RequestUri?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static ValhallaRoutingService Valhalla(Handler handler, string? baseUrl) =>
        new(new HttpClient(handler),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Valhalla:BaseUrl"] = baseUrl }).Build(),
            NullLogger<ValhallaRoutingService>.Instance);

    [Fact]
    public async Task Valhalla_response_is_parsed_and_motor_scooter_is_requested()
    {
        const string json = """{"trip":{"summary":{"length":3.2,"time":780.4},"legs":[{"shape":"_p~iF~ps|U"}]}}""";
        var handler = new Handler(HttpStatusCode.OK, json);

        var route = await Valhalla(handler, "http://localhost:8002/").GetRouteAsync(10.777, 106.701, 10.8031, 106.7147);

        Assert.NotNull(route);
        Assert.Equal(3200.0, route!.DistanceMeters);
        Assert.Equal(780.4, route.DurationSeconds);
        Assert.Equal("_p~iF~ps|U", route.Polyline);
        Assert.Equal("http://localhost:8002/route", handler.Url);
        Assert.Contains("motor_scooter", handler.Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "{}")]
    [InlineData(HttpStatusCode.OK, """{"trip":{"summary":{"length":1,"time":1},"legs":[]}}""")]
    [InlineData(HttpStatusCode.OK, "not json")]
    public async Task Valhalla_failure_or_bad_body_gives_null(HttpStatusCode status, string body)
    {
        Assert.Null(await Valhalla(new Handler(status, body), "http://localhost:8002").GetRouteAsync(10.777, 106.701, 10.8031, 106.7147));
    }

    [Fact]
    public async Task Valhalla_unreachable_gives_null()
    {
        Assert.Null(await Valhalla(new Handler(HttpStatusCode.OK, "{}", throws: true), "http://localhost:8002").GetRouteAsync(10.777, 106.701, 10.8031, 106.7147));
    }

    [Fact]
    public async Task No_base_url_uses_the_fake_straight_line()
    {
        var handler = new Handler(HttpStatusCode.OK, "{}");

        var route = await Valhalla(handler, "").GetRouteAsync(10.7769, 106.7009, 10.8031, 106.7147);

        Assert.NotNull(route);
        Assert.Null(handler.Url); // no HTTP call at all
        Assert.InRange(route!.DistanceMeters, 3000, 3600);
        Assert.False(string.IsNullOrEmpty(route.Polyline));
    }

    [Fact]
    public void Fake_polyline_matches_the_known_google_example()
    {
        // https://developers.google.com/maps/documentation/utilities/polylinealgorithm uses precision 5; scale to 6 by checking round trip shape instead
        var encoded = FakeRoutingService.EncodePolyline6([(38.5, -120.2), (40.7, -120.95)]);
        Assert.Equal("_izlhA~rlgdF_{geC~ywl@", encoded);
    }
}
