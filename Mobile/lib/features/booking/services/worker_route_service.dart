import '../../../core/network/api_client.dart';

/// The worker's way to the house (contract dispatch.md 3.2, `GET /api/v1/dispatch/assignments/{id}/route`, MOB-ORDER-03).
/// The app shows the distance and the time only (no map, see #887).
class WorkerRoute {
  final int assignmentId;
  final double distanceMeters;
  final double durationSeconds;

  const WorkerRoute({
    required this.assignmentId,
    required this.distanceMeters,
    required this.durationSeconds,
  });

  factory WorkerRoute.fromJson(Map<String, dynamic> json) => WorkerRoute(
    assignmentId: (json['assignmentId'] as num).toInt(),
    distanceMeters: (json['distanceMeters'] as num).toDouble(),
    durationSeconds: (json['durationSeconds'] as num).toDouble(),
  );
}

/// What the "on the way" card shows: a route, or a sentence when there is none to show.
class WorkerRouteView {
  final WorkerRoute? route;
  final String? message;

  const WorkerRouteView.route(WorkerRoute this.route) : message = null;
  const WorkerRouteView.message(String this.message) : route = null;
}

abstract class IWorkerRouteService {
  /// The card of one assignment, or null when there is no card to show (the job is no longer on its way, not found).
  Future<WorkerRouteView?> getRoute(int assignmentId);
}

class WorkerRouteService implements IWorkerRouteService {
  final ApiClient _client;

  WorkerRouteService({ApiClient? client}) : _client = client ?? ApiClient();

  @override
  Future<WorkerRouteView?> getRoute(int assignmentId) async {
    try {
      final response = await _client.get<WorkerRoute>(
        '/api/v1/dispatch/assignments/$assignmentId/route',
        fromJson: (json) => WorkerRoute.fromJson(json as Map<String, dynamic>),
      );
      final route = response.data;
      return route == null
          ? const WorkerRouteView.message(routeUnavailableText)
          : WorkerRouteView.route(route);
    } on ApiException catch (e) {
      return routeViewForError(e);
    }
  }
}

const String routeUnavailableText = 'Chưa tính được lộ trình.';

/// Same rule as the Web (`routeProblem`): no location yet is said, a job no longer on its way shows nothing.
WorkerRouteView? routeViewForError(ApiException e) {
  if (e.statusCode == 409 && e.code == 'WORKER_LOCATION_UNKNOWN') {
    return const WorkerRouteView.message('Chưa có vị trí của người làm.');
  }
  if (e.statusCode == 409 || e.statusCode == 404) return null;
  return const WorkerRouteView.message(routeUnavailableText);
}

/// `850 m`, `2,3 km` (same as the Web `formatDistance`).
String formatRouteDistance(double meters) {
  if (meters < 1000) return '${meters.round()} m';
  return '${(meters / 1000).toStringAsFixed(1).replaceAll('.', ',')} km';
}

/// `~12 phút`, `~1 giờ 5 phút`, `~1 giờ`; under a minute is `~1 phút` (same as the Web `formatDuration`).
String formatRouteDuration(double seconds) {
  final minutes = (seconds / 60).round() < 1 ? 1 : (seconds / 60).round();
  if (minutes < 60) return '~$minutes phút';
  final h = minutes ~/ 60;
  final m = minutes % 60;
  return m == 0 ? '~$h giờ' : '~$h giờ $m phút';
}
