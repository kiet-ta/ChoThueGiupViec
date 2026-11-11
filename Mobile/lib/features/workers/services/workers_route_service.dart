import 'package:url_launcher/url_launcher.dart';

import '../../../core/network/api_client.dart';
import '../../../core/network/api_response.dart';
import '../models/worker_route_models.dart';

/// Reads the ways to the customer's house (contract workers.md §2.3.5, `GET /api/v1/workers/assignments/{id}/route`).
/// 404 for a job that is not the worker's, 409 `ROUTE_NOT_AVAILABLE` once the work started, 502 when the routing engine is down.
class WorkersRouteService {
  final ApiClient _apiClient;

  WorkersRouteService({ApiClient? apiClient})
    : _apiClient = apiClient ?? ApiClient();

  /// Up to 3 routes from ([fromLat], [fromLng]) to the house of the job, the fastest first.
  Future<ApiResponse<WorkerRouteDto>> getRoutes(
    int assignmentId, {
    required double fromLat,
    required double fromLng,
  }) {
    return _apiClient.get<WorkerRouteDto>(
      '/api/v1/workers/assignments/$assignmentId/route',
      queryParams: {
        'fromLat': fromLat.toStringAsFixed(6),
        'fromLng': fromLng.toStringAsFixed(6),
      },
      fromJson: (json) => WorkerRouteDto.fromJson(json as Map<String, dynamic>),
    );
  }
}

/// Opens the phone's map app (behind an interface so tests do not need a platform).
abstract class IMapLauncher {
  /// True when a map app took the link.
  Future<bool> open(Uri uri);
}

/// `url_launcher` behind [IMapLauncher]: the link goes to an external map app, not to an in-app browser.
class UrlMapLauncher implements IMapLauncher {
  const UrlMapLauncher();

  @override
  Future<bool> open(Uri uri) async {
    try {
      return await launchUrl(uri, mode: LaunchMode.externalApplication);
    } catch (_) {
      return false;
    }
  }
}
