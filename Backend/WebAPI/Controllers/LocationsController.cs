using CommonService.Application.Common.Models;
using CommonService.Application.Features.Locations.Dtos;
using CommonService.Application.Interfaces.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CommonService.WebAPI.Controllers;

/// <summary>
/// Location services including reverse geocoding via Goong Maps.
/// Protected by [Authorize] to prevent public scraper abuse of the map API key.
/// </summary>
[ApiController]
[Route("api/v1/locations")]
[Authorize]
public class LocationsController : ControllerBase
{
    private readonly IReverseGeocodeService _geocodingService;

    public LocationsController(IReverseGeocodeService geocodingService)
    {
        _geocodingService = geocodingService;
    }

    /// <summary>
    /// Reverse geocodes GPS coordinates into a structured Vietnamese address (City, District/Ward, AddressLine).
    /// </summary>
    [HttpPost("reverse-geocode")]
    [ProducesResponseType(typeof(ApiResponse<ReverseGeocodeResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ReverseGeocode([FromBody] ReverseGeocodeRequest? request, CancellationToken ct)
    {
        if (request == null)
        {
            return BadRequest(ApiResponse<object>.Fail("Dữ liệu toạ độ không hợp lệ."));
        }

        if (request.Latitude < -90 || request.Latitude > 90 || request.Longitude < -180 || request.Longitude > 180)
        {
            return BadRequest(ApiResponse<object>.Fail("Toạ độ GPS không hợp lệ (latitude: -90..90, longitude: -180..180)."));
        }

        var result = await _geocodingService.ReverseGeocodeAsync(request.Latitude, request.Longitude, ct);
        if (result == null)
        {
            return Ok(ApiResponse<ReverseGeocodeResult?>.Fail(
                "Không tìm thấy thông tin địa chỉ phù hợp cho toạ độ này.",
                new ReverseGeocodeResult
                {
                    Latitude = request.Latitude,
                    Longitude = request.Longitude
                }));
        }

        return Ok(ApiResponse<ReverseGeocodeResult>.Ok(result, "Nhận diện địa chỉ thành công."));
    }
}
