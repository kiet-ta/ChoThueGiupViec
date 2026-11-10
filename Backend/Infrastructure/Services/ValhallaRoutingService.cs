using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommonService.Application.Features.Dispatch;
using CommonService.Infrastructure.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CommonService.Infrastructure.Services;

/// <summary>
/// Road routing through a self-hosted Valhalla (OpenStreetMap data), costing <c>motor_scooter</c> because workers ride motorbikes
/// (ROUTE-WEB-01, #765). Valhalla runs from <c>Valhalla:BaseUrl</c> (for example <c>http://localhost:8002</c>). With no BaseUrl
/// it answers with <see cref="FakeRoutingService"/> so Development and CI need no Valhalla. When Valhalla is configured but does
/// not answer, the result is null (the endpoint then answers 502); a fake straight line would show a wrong route as if it were real.
/// </summary>
public class ValhallaRoutingService : IRoutingService
{
    public const string BaseUrlKey = "Valhalla:BaseUrl";
    public const string Costing = "motor_scooter";

    private readonly HttpClient _http;
    private readonly string? _baseUrl;
    private readonly ILogger<ValhallaRoutingService> _logger;
    private readonly FakeRoutingService _fake = new();

    public ValhallaRoutingService(HttpClient http, IConfiguration configuration, ILogger<ValhallaRoutingService> logger)
    {
        _http = http;
        _logger = logger;
        var raw = configuration[BaseUrlKey];
        _baseUrl = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim().TrimEnd('/');
    }

    public async Task<RouteResult?> GetRouteAsync(double fromLat, double fromLng, double toLat, double toLng, CancellationToken ct = default)
    {
        if (_baseUrl is null)
        {
            return await _fake.GetRouteAsync(fromLat, fromLng, toLat, toLng, ct);
        }

        try
        {
            var body = new
            {
                locations = new[]
                {
                    new { lat = fromLat, lon = fromLng },
                    new { lat = toLat, lon = toLng },
                },
                costing = Costing,
                directions_options = new { units = "kilometers" },
            };

            using var response = await _http.PostAsJsonAsync($"{_baseUrl}/route", body, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Valhalla returned HTTP {StatusCode} for ({FromLat}, {FromLng}) -> ({ToLat}, {ToLng}).",
                    (int)response.StatusCode, fromLat, fromLng, toLat, toLng);
                return null;
            }

            var root = await response.Content.ReadFromJsonAsync<ValhallaRouteResponse>(cancellationToken: ct);
            return ParseRoute(root);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Valhalla route request failed; no route returned.");
            return null;
        }
    }

    /// <summary>Reads distance (km -> m), time (s) and the first leg's encoded shape; null when any part is missing.</summary>
    public static RouteResult? ParseRoute(ValhallaRouteResponse? root)
    {
        var trip = root?.Trip;
        var shape = trip?.Legs?.FirstOrDefault()?.Shape;
        if (trip?.Summary is null || string.IsNullOrEmpty(shape))
        {
            return null;
        }

        return new RouteResult(
            Math.Round(trip.Summary.Length * 1000.0, 1),
            Math.Round(trip.Summary.Time, 1),
            shape);
    }

    public class ValhallaRouteResponse
    {
        [JsonPropertyName("trip")]
        public ValhallaTrip? Trip { get; set; }
    }

    public class ValhallaTrip
    {
        [JsonPropertyName("summary")]
        public ValhallaSummary? Summary { get; set; }

        [JsonPropertyName("legs")]
        public List<ValhallaLeg>? Legs { get; set; }
    }

    public class ValhallaSummary
    {
        /// <summary>Length in kilometres (the request asks for <c>units = kilometers</c>).</summary>
        [JsonPropertyName("length")]
        public double Length { get; set; }

        /// <summary>Travel time in seconds.</summary>
        [JsonPropertyName("time")]
        public double Time { get; set; }
    }

    public class ValhallaLeg
    {
        [JsonPropertyName("shape")]
        public string? Shape { get; set; }
    }
}
