/// One way from the worker to the customer's house: road distance, travel time and the encoded line (6 decimals).
class WorkerRouteOptionDto {
  final double distanceMeters;
  final double durationSeconds;
  final String polyline;

  const WorkerRouteOptionDto({
    required this.distanceMeters,
    required this.durationSeconds,
    required this.polyline,
  });

  factory WorkerRouteOptionDto.fromJson(Map<String, dynamic> json) =>
      WorkerRouteOptionDto(
        distanceMeters: (json['distanceMeters'] as num?)?.toDouble() ?? 0.0,
        durationSeconds: (json['durationSeconds'] as num?)?.toDouble() ?? 0.0,
        polyline: json['polyline'] as String? ?? '',
      );
}

/// The ways to the house of one accepted job (contract workers.md §2.3.5, `GET /api/v1/workers/assignments/{id}/route`).
/// The first route is the fastest.
class WorkerRouteDto {
  final int assignmentId;
  final double destinationLatitude;
  final double destinationLongitude;
  final List<WorkerRouteOptionDto> routes;

  const WorkerRouteDto({
    required this.assignmentId,
    required this.destinationLatitude,
    required this.destinationLongitude,
    required this.routes,
  });

  factory WorkerRouteDto.fromJson(Map<String, dynamic> json) {
    final raw = json['routes'];
    return WorkerRouteDto(
      assignmentId: (json['assignmentId'] as num?)?.toInt() ?? 0,
      destinationLatitude:
          (json['destinationLatitude'] as num?)?.toDouble() ?? 0.0,
      destinationLongitude:
          (json['destinationLongitude'] as num?)?.toDouble() ?? 0.0,
      routes: raw is List
          ? raw
                .whereType<Map<String, dynamic>>()
                .map(WorkerRouteOptionDto.fromJson)
                .toList()
          : const [],
    );
  }
}

/// `850 m` below 1 km, otherwise kilometres with one decimal and a comma (`3,2 km`).
String formatRouteDistance(double meters) {
  if (meters < 1000) return '${meters.round()} m';
  return '${(meters / 1000).toStringAsFixed(1).replaceAll('.', ',')} km';
}

/// `~12 phút`, `~1 giờ 5 phút`, `~1 giờ`; under a minute is `~1 phút`.
String formatRouteDuration(double seconds) {
  final minutes = (seconds / 60).round().clamp(1, 1 << 30);
  if (minutes < 60) return '~$minutes phút';
  final h = minutes ~/ 60;
  final m = minutes % 60;
  return m == 0 ? '~$h giờ' : '~$h giờ $m phút';
}

/// The label of a route in the list: `Đường 1 · 3,2 km · ~12 phút`.
String routeOptionLabel(int index, WorkerRouteOptionDto route) =>
    'Đường ${index + 1} · ${formatRouteDistance(route.distanceMeters)} · '
    '${formatRouteDuration(route.durationSeconds)}';

/// The address of the map link: the exact point when the job has coordinates, the written address otherwise.
/// Returns null when the job has neither (`0,0` and an empty address).
Uri? mapDirectionsUri({
  required double latitude,
  required double longitude,
  required String address,
}) {
  final hasPoint = latitude != 0.0 || longitude != 0.0;
  final destination = hasPoint
      ? '$latitude,$longitude'
      : (address.trim().isEmpty ? null : address.trim());
  if (destination == null) return null;
  return Uri.https('www.google.com', '/maps/dir/', {
    'api': '1',
    'destination': destination,
    'travelmode': 'two-wheeler',
  });
}

/// Decodes an encoded polyline with 6 decimals (the Valhalla format) into (latitude, longitude) pairs.
List<(double, double)> decodePolyline6(String encoded) {
  final points = <(double, double)>[];
  var index = 0;
  var lat = 0;
  var lng = 0;
  // A truncated value ends the line instead of throwing.
  int? next() {
    var result = 0;
    var shift = 0;
    while (index < encoded.length) {
      final byte = encoded.codeUnitAt(index++) - 63;
      result |= (byte & 0x1f) << shift;
      shift += 5;
      if (byte < 0x20) {
        return (result & 1) != 0 ? ~(result >> 1) : result >> 1;
      }
    }
    return null;
  }

  while (index < encoded.length) {
    final dLat = next();
    final dLng = next();
    if (dLat == null || dLng == null) break;
    lat += dLat;
    lng += dLng;
    points.add((lat / 1e6, lng / 1e6));
  }
  return points;
}
