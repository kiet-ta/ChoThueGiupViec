using CommonService.Application.Interfaces.Ports;
using CommonService.Infrastructure.Services;

namespace CommonService.Tests.Infrastructure;

public class HaversineGeoServiceTests
{
    private readonly HaversineGeoService _geo = new();

    [Fact]
    public void Distance_between_same_point_is_zero()
    {
        var point = new GeoPoint(10.7716, 106.7044);
        var distance = _geo.DistanceMeters(point, point);

        Assert.Equal(0.0, distance, 4);
    }

    [Fact]
    public void Distance_is_symmetric()
    {
        var p1 = new GeoPoint(10.7950, 106.7218); // Landmark 81
        var p2 = new GeoPoint(10.7716, 106.7044); // Bitexco
        var d1 = _geo.DistanceMeters(p1, p2);
        var d2 = _geo.DistanceMeters(p2, p1);

        Assert.Equal(d1, d2, 4);
    }

    [Fact]
    public void Distance_between_Landmark81_and_Bitexco_is_approximately_3200_meters()
    {
        // Landmark 81: 10.7950° N, 106.7218° E
        // Bitexco: 10.7716° N, 106.7044° E
        // Expected distance: ~3,200m
        var landmark81 = new GeoPoint(10.7950, 106.7218);
        var bitexco = new GeoPoint(10.7716, 106.7044);

        var distance = _geo.DistanceMeters(landmark81, bitexco);

        Assert.InRange(distance, 3100.0, 3350.0);
    }

    [Fact]
    public void Throws_ArgumentException_when_coordinates_contain_NaN()
    {
        var valid = new GeoPoint(10.0, 106.0);
        var nanLat = new GeoPoint(double.NaN, 106.0);
        var nanLon = new GeoPoint(10.0, double.NaN);

        Assert.Throws<ArgumentException>(() => _geo.DistanceMeters(valid, nanLat));
        Assert.Throws<ArgumentException>(() => _geo.DistanceMeters(nanLon, valid));
    }
}
