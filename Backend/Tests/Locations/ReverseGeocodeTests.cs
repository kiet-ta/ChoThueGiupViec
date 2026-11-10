using CommonService.Application.Common.Models;
using CommonService.Application.Features.Locations.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Services;
using CommonService.WebAPI.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Tests.Locations;

public class ReverseGeocodeTests
{
    [Fact]
    public async Task FakeReverseGeocode_resolves_HCMC_vicinity()
    {
        var service = new FakeReverseGeocodeService();
        var result = await service.ReverseGeocodeAsync(10.776889, 106.700806);

        Assert.NotNull(result);
        Assert.Equal("Thành phố Hồ Chí Minh", result.City);
        Assert.Equal("Phường Bến Nghé", result.District);
        Assert.Contains("Lê Lợi", result.AddressLine);
        Assert.Contains("Quận 1", result.AddressLine);
    }

    [Fact]
    public async Task FakeReverseGeocode_resolves_Hanoi_vicinity()
    {
        var service = new FakeReverseGeocodeService();
        var result = await service.ReverseGeocodeAsync(21.0285, 105.8542);

        Assert.NotNull(result);
        Assert.Equal("Thành phố Hà Nội", result.City);
        Assert.Equal("Phường Hoàn Kiếm", result.District);
        Assert.Contains("Tràng Tiền", result.AddressLine);
    }

    [Fact]
    public async Task FakeReverseGeocode_returns_null_outside_Vietnam()
    {
        var service = new FakeReverseGeocodeService();

        var ocean = await service.ReverseGeocodeAsync(0.0, 0.0);
        Assert.Null(ocean);

        var tokyo = await service.ReverseGeocodeAsync(35.6762, 139.6503);
        Assert.Null(tokyo);
    }

    [Fact]
    public async Task FakeReverseGeocode_override_handler_works()
    {
        var service = new FakeReverseGeocodeService();
        service.Override((lat, lng) => new ReverseGeocodeResult
        {
            City = "Thành phố Đà Nẵng",
            District = "Phường Hải Châu",
            AddressLine = "Custom Test St",
            Latitude = lat,
            Longitude = lng
        });

        var res = await service.ReverseGeocodeAsync(1.0, 1.0);
        Assert.NotNull(res);
        Assert.Equal("Custom Test St", res.AddressLine);

        service.Reset();
        var afterReset = await service.ReverseGeocodeAsync(1.0, 1.0);
        Assert.Null(afterReset);
    }

    [Fact]
    public void Osm_parser_normalizes_components_correctly()
    {
        var props = new OsmReverseGeocodeService.OsmPhotonProperties
        {
            HouseNumber = "17",
            Street = "Phố Trần Nguyên Hãn",
            Locality = "Khu phố cổ",
            District = "Hoàn Kiếm",
            City = "Hà Nội",
            Country = "Việt Nam",
            Postcode = "11024"
        };

        var parsed = OsmReverseGeocodeService.ParsePhotonResult(props, 21.0285, 105.8542);

        Assert.Equal("Thành phố Hà Nội", parsed.City);
        Assert.Equal("Hoàn Kiếm", parsed.District);
        Assert.Equal("17 Phố Trần Nguyên Hãn", parsed.AddressLine);
        Assert.Contains("17 Phố Trần Nguyên Hãn", parsed.FormattedAddress);
        Assert.Contains("Hoàn Kiếm", parsed.FormattedAddress);
        Assert.Contains("Thành phố Hà Nội", parsed.FormattedAddress);
        Assert.Equal(21.0285, parsed.Latitude);
        Assert.Equal(105.8542, parsed.Longitude);
    }

    [Fact]
    public async Task OsmService_fallback_to_Fake_on_error_or_offline()
    {
        var service = new OsmReverseGeocodeService(
            new HttpClient(),
            NullLogger<OsmReverseGeocodeService>.Instance);

        var result = await service.ReverseGeocodeAsync(10.7769, 106.7009);
        Assert.NotNull(result);
        Assert.Equal("Thành phố Hồ Chí Minh", result.City);
        Assert.NotNull(result.District);
    }
}

public class LocationsControllerTests
{
    [Fact]
    public async Task ReverseGeocode_rejects_null_request()
    {
        var controller = new LocationsController(new FakeReverseGeocodeService());
        var res = await controller.ReverseGeocode(null, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(res);
        var envelope = Assert.IsType<ApiResponse<object>>(bad.Value);
        Assert.False(envelope.Success);
    }

    [Theory]
    [InlineData(-91.0, 106.0)]
    [InlineData(91.0, 106.0)]
    [InlineData(10.0, -181.0)]
    [InlineData(10.0, 181.0)]
    public async Task ReverseGeocode_rejects_out_of_range_coordinates(double lat, double lng)
    {
        var controller = new LocationsController(new FakeReverseGeocodeService());
        var res = await controller.ReverseGeocode(new ReverseGeocodeRequest { Latitude = lat, Longitude = lng }, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(res);
        var envelope = Assert.IsType<ApiResponse<object>>(bad.Value);
        Assert.False(envelope.Success);
    }

    [Fact]
    public async Task ReverseGeocode_returns_ok_with_success_true_when_found()
    {
        var controller = new LocationsController(new FakeReverseGeocodeService());
        var res = await controller.ReverseGeocode(new ReverseGeocodeRequest { Latitude = 10.7769, Longitude = 106.7009 }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(res);
        var envelope = Assert.IsType<ApiResponse<ReverseGeocodeResult>>(ok.Value);
        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.Equal("Thành phố Hồ Chí Minh", envelope.Data.City);
        Assert.Equal("Phường Bến Nghé", envelope.Data.District);
        Assert.Equal(10.7769, envelope.Data.Latitude);
    }

    [Fact]
    public async Task ReverseGeocode_returns_ok_with_success_false_and_retains_coordinates_when_not_found()
    {
        var fake = new FakeReverseGeocodeService();
        var controller = new LocationsController(fake);
        // Point in the Pacific Ocean
        var res = await controller.ReverseGeocode(new ReverseGeocodeRequest { Latitude = 0.0, Longitude = 0.0 }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(res);
        var envelope = Assert.IsType<ApiResponse<ReverseGeocodeResult?>>(ok.Value);
        Assert.False(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.Equal(0.0, envelope.Data.Latitude);
        Assert.Equal(0.0, envelope.Data.Longitude);
    }
}
