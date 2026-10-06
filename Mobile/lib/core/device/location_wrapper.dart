import 'dart:math' as math;

/// Represents a geographic position with latitude, longitude and accuracy.
class GeoPoint {
  final double latitude;
  final double longitude;
  final double accuracyMeters;
  final DateTime timestamp;

  const GeoPoint({
    required this.latitude,
    required this.longitude,
    this.accuracyMeters = 5.0,
    required this.timestamp,
  });

  Map<String, dynamic> toJson() => {
        'latitude': latitude,
        'longitude': longitude,
        'accuracyMeters': accuracyMeters,
        'timestamp': timestamp.toIso8601String(),
      };
}

/// Abstract contract for Location retrieval and distance calculations.
abstract class ILocationWrapper {
  /// Retrieves the current device GPS coordinate.
  Future<GeoPoint> getCurrentLocation();

  /// Computes the great-circle distance between two points in meters using Haversine formula.
  double calculateDistanceMeters(
    double lat1,
    double lon1,
    double lat2,
    double lon2,
  );

  /// Checks if two coordinates are within a specified distance threshold (e.g. <=100m for check-in).
  bool isWithinRadius(
    double lat1,
    double lon1,
    double lat2,
    double lon2,
    double maxDistanceMeters,
  );
}

/// Standard LocationWrapper with Haversine distance calculation and testable mock capability.
class LocationWrapper implements ILocationWrapper {
  GeoPoint? _mockCurrentLocation;

  LocationWrapper({GeoPoint? mockLocation}) : _mockCurrentLocation = mockLocation;

  /// Sets mock location for automated testing.
  void setMockLocation(GeoPoint point) {
    _mockCurrentLocation = point;
  }

  @override
  Future<GeoPoint> getCurrentLocation() async {
    if (_mockCurrentLocation != null) {
      return _mockCurrentLocation!;
    }

    // Default fallback position (e.g. Hanoi center) if GPS sensor is not available
    return GeoPoint(
      latitude: 21.028511,
      longitude: 105.854167,
      accuracyMeters: 5.0,
      timestamp: DateTime.now().toUtc(),
    );
  }

  @override
  double calculateDistanceMeters(
    double lat1,
    double lon1,
    double lat2,
    double lon2,
  ) {
    const double earthRadiusMeters = 6371000.0;

    final double dLat = (lat2 - lat1) * (math.pi / 180.0);
    final double dLon = (lon2 - lon1) * (math.pi / 180.0);

    final double lat1Rad = lat1 * (math.pi / 180.0);
    final double lat2Rad = lat2 * (math.pi / 180.0);

    final double a = math.sin(dLat / 2) * math.sin(dLat / 2) +
        math.cos(lat1Rad) *
            math.cos(lat2Rad) *
            math.sin(dLon / 2) *
            math.sin(dLon / 2);

    final double c = 2 * math.atan2(math.sqrt(a), math.sqrt(1 - a));

    return earthRadiusMeters * c;
  }

  @override
  bool isWithinRadius(
    double lat1,
    double lon1,
    double lat2,
    double lon2,
    double maxDistanceMeters,
  ) {
    final distance = calculateDistanceMeters(lat1, lon1, lat2, lon2);
    return distance <= maxDistanceMeters;
  }
}
