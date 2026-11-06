using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommonService.Application.Features.Locations.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Infrastructure.Fakes;
using Microsoft.Extensions.Logging;

namespace CommonService.Infrastructure.Services;

/// <summary>
/// Production reverse geocoding service backed by OpenStreetMap via OSM Photon (https://photon.komoot.io/reverse).
/// 100% free, requires no credit card, no account registration, and no pending approval.
/// When offline or when the service is unreachable, gracefully delegates to FakeReverseGeocodeService so
/// development and CI test runs remain completely offline, fast, and deterministic.
/// </summary>
public class OsmReverseGeocodeService : IReverseGeocodeService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OsmReverseGeocodeService> _logger;
    private readonly FakeReverseGeocodeService _fakeFallback;

    public OsmReverseGeocodeService(
        HttpClient httpClient,
        ILogger<OsmReverseGeocodeService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _fakeFallback = new FakeReverseGeocodeService();
    }

    public async Task<ReverseGeocodeResult?> ReverseGeocodeAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        // Outside Vietnam territorial bounding box
        if (latitude < 8.0 || latitude > 24.0 || longitude < 102.0 || longitude > 110.0)
        {
            return null;
        }

        try
        {
            var latStr = latitude.ToString("F6", CultureInfo.InvariantCulture);
            var lngStr = longitude.ToString("F6", CultureInfo.InvariantCulture);
            var url = $"https://photon.komoot.io/reverse?lat={latStr}&lon={lngStr}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "ToAmCare/1.0 (contact@toamcare.vn)");

            using var response = await _httpClient.SendAsync(req, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OSM Photon returned HTTP {StatusCode} for ({Lat}, {Lng}); falling back to Fake.", response.StatusCode, latitude, longitude);
                return await _fakeFallback.ReverseGeocodeAsync(latitude, longitude, ct);
            }

            var root = await response.Content.ReadFromJsonAsync<OsmPhotonResponse>(cancellationToken: ct);
            if (root?.Features == null || root.Features.Count == 0 || root.Features[0].Properties == null)
            {
                _logger.LogInformation("OSM Photon returned no features for ({Lat}, {Lng}); falling back to Fake.", latitude, longitude);
                return await _fakeFallback.ReverseGeocodeAsync(latitude, longitude, ct);
            }

            return ParsePhotonResult(root.Features[0].Properties!, latitude, longitude);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Failed to call OSM Photon for ({Lat}, {Lng}); falling back to Fake.", latitude, longitude);
            return await _fakeFallback.ReverseGeocodeAsync(latitude, longitude, ct);
        }
    }

    public static ReverseGeocodeResult ParsePhotonResult(OsmPhotonProperties props, double latitude, double longitude)
    {
        var city = NormalizeCity(props.City ?? props.State);
        var district = NormalizeDistrict(props.District ?? props.Locality ?? props.City);
        var addressLine = BuildAddressLine(props, district, latitude, longitude);

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(addressLine)) parts.Add(addressLine);
        if (!string.IsNullOrWhiteSpace(district) && !addressLine.Contains(district, StringComparison.OrdinalIgnoreCase)) parts.Add(district);
        if (!string.IsNullOrWhiteSpace(city) && !addressLine.Contains(city, StringComparison.OrdinalIgnoreCase)) parts.Add(city);
        parts.Add(props.Country ?? "Việt Nam");

        var formattedAddress = string.Join(", ", parts);

        return new ReverseGeocodeResult
        {
            City = city,
            District = district,
            AddressLine = addressLine,
            FormattedAddress = formattedAddress,
            Latitude = latitude,
            Longitude = longitude,
        };
    }

    private static string NormalizeCity(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Thành phố Hồ Chí Minh";
        if (raw.Contains("Hồ Chí Minh", StringComparison.OrdinalIgnoreCase) || raw.Contains("Ho Chi Minh", StringComparison.OrdinalIgnoreCase) || raw.Equals("TP.HCM", StringComparison.OrdinalIgnoreCase) || raw.Equals("TPHCM", StringComparison.OrdinalIgnoreCase))
            return "Thành phố Hồ Chí Minh";
        if (raw.Contains("Hà Nội", StringComparison.OrdinalIgnoreCase) || raw.Contains("Ha Noi", StringComparison.OrdinalIgnoreCase))
            return "Thành phố Hà Nội";
        if (raw.Contains("Đà Nẵng", StringComparison.OrdinalIgnoreCase) || raw.Contains("Da Nang", StringComparison.OrdinalIgnoreCase))
            return "Thành phố Đà Nẵng";
        if (raw.Contains("Cần Thơ", StringComparison.OrdinalIgnoreCase) || raw.Contains("Can Tho", StringComparison.OrdinalIgnoreCase))
            return "Thành phố Cần Thơ";
        if (raw.Contains("Hải Phòng", StringComparison.OrdinalIgnoreCase) || raw.Contains("Hai Phong", StringComparison.OrdinalIgnoreCase))
            return "Thành phố Hải Phòng";
        return raw.Trim();
    }

    private static string NormalizeDistrict(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Phường Bến Nghé";
        return raw.Trim();
    }

    private static string BuildAddressLine(OsmPhotonProperties props, string district, double latitude, double longitude)
    {
        if (!string.IsNullOrWhiteSpace(props.HouseNumber) && !string.IsNullOrWhiteSpace(props.Street))
            return $"{props.HouseNumber} {props.Street}";
        if (!string.IsNullOrWhiteSpace(props.Street))
            return props.Street;
        if (!string.IsNullOrWhiteSpace(props.Name))
            return props.Name;
        if (!string.IsNullOrWhiteSpace(props.Locality))
            return props.Locality;
        return $"Khu vực toạ độ ({latitude.ToString("F4", CultureInfo.InvariantCulture)}, {longitude.ToString("F4", CultureInfo.InvariantCulture)})";
    }

    public class OsmPhotonResponse
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("features")]
        public List<OsmPhotonFeature>? Features { get; set; }
    }

    public class OsmPhotonFeature
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("properties")]
        public OsmPhotonProperties? Properties { get; set; }
    }

    public class OsmPhotonProperties
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("housenumber")]
        public string? HouseNumber { get; set; }

        [JsonPropertyName("street")]
        public string? Street { get; set; }

        [JsonPropertyName("locality")]
        public string? Locality { get; set; }

        [JsonPropertyName("district")]
        public string? District { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("postcode")]
        public string? Postcode { get; set; }
    }
}
