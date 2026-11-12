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
    private readonly double _matrixTimeoutSeconds;

    public ValhallaRoutingService(HttpClient http, IConfiguration configuration, ILogger<ValhallaRoutingService> logger)
    {
        _http = http;
        _logger = logger;
        var raw = configuration[BaseUrlKey];
        _baseUrl = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim().TrimEnd('/');
        _matrixTimeoutSeconds = double.TryParse(configuration[MatrixTimeoutKey], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var t) && t > 0 ? t : DefaultMatrixTimeoutSeconds;
    }

    public async Task<RouteResult?> GetRouteAsync(double fromLat, double fromLng, double toLat, double toLng, CancellationToken ct = default) =>
        (await GetRoutesAsync(fromLat, fromLng, toLat, toLng, 1, ct)).FirstOrDefault();

    /// <summary>Up to <paramref name="maxRoutes"/> routes (Valhalla <c>alternates</c>), sorted by duration; empty when Valhalla does not answer.</summary>
    public async Task<IReadOnlyList<RouteResult>> GetRoutesAsync(
        double fromLat, double fromLng, double toLat, double toLng, int maxRoutes, CancellationToken ct = default)
    {
        maxRoutes = Math.Clamp(maxRoutes, 1, 3);
        if (_baseUrl is null)
        {
            var fake = await _fake.GetRouteAsync(fromLat, fromLng, toLat, toLng, ct);
            return fake is null ? [] : [fake];
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
                alternates = maxRoutes - 1,
            };

            using var response = await _http.PostAsJsonAsync($"{_baseUrl}/route", body, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Valhalla returned HTTP {StatusCode} for ({FromLat}, {FromLng}) -> ({ToLat}, {ToLng}).",
                    (int)response.StatusCode, fromLat, fromLng, toLat, toLng);
                return [];
            }

            var root = await response.Content.ReadFromJsonAsync<ValhallaRouteResponse>(cancellationToken: ct);
            return ParseRoutes(root, maxRoutes);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Valhalla route request failed; no route returned.");
            return [];
        }
    }

    /// <summary>Config key for the matrix timeout in seconds (default <see cref="DefaultMatrixTimeoutSeconds"/>).</summary>
    public const string MatrixTimeoutKey = "Valhalla:MatrixTimeoutSeconds";
    public const double DefaultMatrixTimeoutSeconds = 3.0;

    /// <summary>
    /// One Valhalla <c>sources_to_targets</c> (matrix) call: road metres from the origin to every target (DISPATCH-ROAD-01). A target with
    /// no route is null in the result; null overall when Valhalla does not answer within the timeout. With no BaseUrl the fake straight line is used.
    /// </summary>
    public async Task<IReadOnlyList<double?>?> GetRoadDistancesAsync(
        double fromLat, double fromLng, IReadOnlyList<(double Lat, double Lng)> targets, CancellationToken ct = default)
    {
        if (targets.Count == 0) return [];
        if (_baseUrl is null) return await _fake.GetRoadDistancesAsync(fromLat, fromLng, targets, ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_matrixTimeoutSeconds));
        try
        {
            var body = new
            {
                sources = new[] { new { lat = fromLat, lon = fromLng } },
                targets = targets.Select(t => new { lat = t.Lat, lon = t.Lng }).ToArray(),
                costing = Costing,
            };

            using var response = await _http.PostAsJsonAsync($"{_baseUrl}/sources_to_targets", body, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Valhalla matrix returned HTTP {StatusCode} for {Count} targets.", (int)response.StatusCode, targets.Count);
                return null;
            }

            var root = await response.Content.ReadFromJsonAsync<ValhallaMatrixResponse>(cancellationToken: timeout.Token);
            return ParseMatrix(root, targets.Count);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            if (ct.IsCancellationRequested) throw;
            _logger.LogWarning(ex, "Valhalla matrix request failed; dispatch falls back to straight-line distance.");
            return null;
        }
    }

    /// <summary>Metres per target (km -> m) from the first source row; null for a target without a distance or missing from the answer.</summary>
    public static IReadOnlyList<double?> ParseMatrix(ValhallaMatrixResponse? root, int targetCount)
    {
        var result = new double?[targetCount];
        var row = root?.SourcesToTargets?.FirstOrDefault();
        foreach (var cell in row ?? [])
        {
            if (cell.ToIndex is >= 0 && cell.ToIndex < targetCount && cell.Distance is not null)
            {
                result[cell.ToIndex.Value] = Math.Round(cell.Distance.Value * 1000.0, 1);
            }
        }

        return result;
    }

    public class ValhallaMatrixResponse
    {
        [JsonPropertyName("sources_to_targets")]
        public List<List<ValhallaMatrixCell>>? SourcesToTargets { get; set; }
    }

    public class ValhallaMatrixCell
    {
        /// <summary>Kilometres (Valhalla's default unit); null when the target cannot be reached.</summary>
        [JsonPropertyName("distance")]
        public double? Distance { get; set; }

        [JsonPropertyName("to_index")]
        public int? ToIndex { get; set; }
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

    /// <summary>The main route and its alternatives, sorted by duration (the fastest first), at most <paramref name="maxRoutes"/>.</summary>
    public static IReadOnlyList<RouteResult> ParseRoutes(ValhallaRouteResponse? root, int maxRoutes)
    {
        var all = new List<RouteResult>();
        var main = ParseRoute(root);
        if (main is not null) all.Add(main);
        foreach (var alt in root?.Alternates ?? [])
        {
            var parsed = ParseRoute(alt);
            if (parsed is not null) all.Add(parsed);
        }

        return all.OrderBy(r => r.DurationSeconds).Take(Math.Max(1, maxRoutes)).ToList();
    }

    public class ValhallaRouteResponse
    {
        [JsonPropertyName("trip")]
        public ValhallaTrip? Trip { get; set; }

        /// <summary>Other routes between the same points; each has its own <c>trip</c>.</summary>
        [JsonPropertyName("alternates")]
        public List<ValhallaRouteResponse>? Alternates { get; set; }
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
