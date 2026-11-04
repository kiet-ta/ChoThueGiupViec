using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommonService.Application.Features.Locations.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Infrastructure.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CommonService.Infrastructure.Services;

/// <summary>
/// Production reverse geocoding service backed by Goong Maps API (https://rsapi.goong.io/Geocode).
/// When Goong:ApiKey is absent or empty, gracefully delegates to FakeReverseGeocodeService so
/// development and CI test runs remain completely offline and deterministic.
/// </summary>
public class GoongReverseGeocodeService : IReverseGeocodeService
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly ILogger<GoongReverseGeocodeService> _logger;
    private readonly FakeReverseGeocodeService _fakeFallback;

    public GoongReverseGeocodeService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<GoongReverseGeocodeService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = configuration["Goong:ApiKey"]
            ?? configuration["Goong_ApiKey"]
            ?? Environment.GetEnvironmentVariable("GOONG__APIKEY")
            ?? Environment.GetEnvironmentVariable("GOONG_API_KEY");
        _fakeFallback = new FakeReverseGeocodeService();
    }

    public async Task<ReverseGeocodeResult?> ReverseGeocodeAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        // If API key is missing or placeholder, delegate to in-memory fake
        if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey.Contains("YOUR_GOONG_API_KEY", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Goong API key not configured; using FakeReverseGeocodeService fallback.");
            return await _fakeFallback.ReverseGeocodeAsync(latitude, longitude, ct);
        }

        try
        {
            var latStr = latitude.ToString("F6", CultureInfo.InvariantCulture);
            var lngStr = longitude.ToString("F6", CultureInfo.InvariantCulture);
            var url = $"https://rsapi.goong.io/Geocode?latlng={latStr},{lngStr}&api_key={Uri.EscapeDataString(_apiKey)}";

            using var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Goong Maps geocoding returned HTTP {StatusCode} for ({Lat}, {Lng})", response.StatusCode, latitude, longitude);
                return null;
            }

            var root = await response.Content.ReadFromJsonAsync<GoongResponse>(cancellationToken: ct);
            if (root == null || root.Status != "OK" || root.Results == null || root.Results.Count == 0)
            {
                _logger.LogInformation("Goong Maps returned status '{Status}' with {Count} results", root?.Status, root?.Results?.Count ?? 0);
                return null;
            }

            var first = root.Results[0];
            return ParseGoongResult(first, latitude, longitude);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogError(ex, "Failed to call Goong Maps Reverse Geocoding API for ({Lat}, {Lng})", latitude, longitude);
            return null;
        }
    }

    public static ReverseGeocodeResult ParseGoongResult(GoongResult item, double latitude, double longitude)
    {
        string? province = null;
        string? ward = null;
        string? oldDistrict = null;
        string? route = null;
        string? streetNumber = null;

        if (item.AddressComponents != null)
        {
            foreach (var comp in item.AddressComponents)
            {
                var types = comp.Types ?? [];
                if (types.Contains("administrative_area_level_1"))
                {
                    province = comp.LongName;
                }
                else if (types.Contains("sublocality_level_1") || types.Contains("sublocality") || types.Contains("ward") || types.Contains("administrative_area_level_3"))
                {
                    ward ??= comp.LongName;
                }
                else if (types.Contains("administrative_area_level_2"))
                {
                    oldDistrict ??= comp.LongName;
                }
                else if (types.Contains("route"))
                {
                    route ??= comp.LongName;
                }
                else if (types.Contains("street_number"))
                {
                    streetNumber ??= comp.LongName;
                }
            }
        }

        var normalizedCity = NormalizeProvince(province);
        var normalizedDistrict = NormalizeWard(ward);
        var addressLine = BuildAddressLine(streetNumber, route, oldDistrict, item.FormattedAddress, normalizedDistrict, normalizedCity);

        return new ReverseGeocodeResult
        {
            City = normalizedCity,
            District = normalizedDistrict,
            AddressLine = addressLine,
            FormattedAddress = item.FormattedAddress ?? string.Empty,
            Latitude = latitude,
            Longitude = longitude,
        };
    }

    private static string NormalizeProvince(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Thành phố Hồ Chí Minh";
        var trimmed = raw.Trim();

        if (trimmed.Contains("Hồ Chí Minh", StringComparison.OrdinalIgnoreCase)) return "Thành phố Hồ Chí Minh";
        if (trimmed.Contains("Hà Nội", StringComparison.OrdinalIgnoreCase)) return "Thành phố Hà Nội";
        if (trimmed.Contains("Đà Nẵng", StringComparison.OrdinalIgnoreCase)) return "Thành phố Đà Nẵng";
        if (trimmed.Contains("Hải Phòng", StringComparison.OrdinalIgnoreCase)) return "Thành phố Hải Phòng";
        if (trimmed.Contains("Cần Thơ", StringComparison.OrdinalIgnoreCase)) return "Thành phố Cần Thơ";
        if (trimmed.Contains("Huế", StringComparison.OrdinalIgnoreCase)) return "Thành phố Huế";

        if (trimmed.StartsWith("Tỉnh ", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("Thành phố ", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return $"Tỉnh {trimmed}";
    }

    private static string NormalizeWard(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var trimmed = raw.Trim();
        return trimmed;
    }

    private static string BuildAddressLine(string? streetNumber, string? route, string? oldDistrict, string? formattedAddress, string ward, string city)
    {
        var streetPart = string.Empty;
        if (!string.IsNullOrWhiteSpace(streetNumber) && !string.IsNullOrWhiteSpace(route))
        {
            streetPart = $"{streetNumber.Trim()} {route.Trim()}";
        }
        else if (!string.IsNullOrWhiteSpace(route))
        {
            streetPart = route.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(streetNumber))
        {
            streetPart = streetNumber.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(formattedAddress))
        {
            // Extract the first part of formattedAddress before the ward/district/city
            var parts = formattedAddress.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && !parts[0].Equals(ward, StringComparison.OrdinalIgnoreCase) && !parts[0].Equals(city, StringComparison.OrdinalIgnoreCase))
            {
                streetPart = parts[0];
            }
        }

        if (string.IsNullOrWhiteSpace(streetPart))
        {
            streetPart = "Địa chỉ tại toạ độ";
        }

        if (!string.IsNullOrWhiteSpace(oldDistrict))
        {
            return $"{streetPart}, {oldDistrict.Trim()}";
        }

        return streetPart;
    }

    public class GoongResponse
    {
        [JsonPropertyName("results")]
        public List<GoongResult>? Results { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }

    public class GoongResult
    {
        [JsonPropertyName("address_components")]
        public List<GoongAddressComponent>? AddressComponents { get; set; }

        [JsonPropertyName("formatted_address")]
        public string? FormattedAddress { get; set; }
    }

    public class GoongAddressComponent
    {
        [JsonPropertyName("long_name")]
        public string? LongName { get; set; }

        [JsonPropertyName("short_name")]
        public string? ShortName { get; set; }

        [JsonPropertyName("types")]
        public List<string>? Types { get; set; }
    }
}
