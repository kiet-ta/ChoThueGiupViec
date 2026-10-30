import '../../../core/network/api_client.dart';
import '../../../core/network/api_response.dart';
import '../models/worker_work_area_models.dart';

/// Reads and saves where the freelancer works (contract workers.md §2.3B, `GET` / `PUT /api/workers/me/work-area`).
class WorkersWorkAreaService {
  final ApiClient _apiClient;

  WorkersWorkAreaService({ApiClient? apiClient})
    : _apiClient = apiClient ?? ApiClient();

  /// The registered area; nothing registered is a successful answer with `isSet` false.
  Future<ApiResponse<WorkerWorkArea>> getWorkArea() {
    return _apiClient.get<WorkerWorkArea>(
      '/api/workers/me/work-area',
      fromJson: (json) => json is Map<String, dynamic>
          ? WorkerWorkArea.fromJson(json)
          : const WorkerWorkArea.empty(),
    );
  }

  /// Replaces the registered area. A refused field comes back as an `ApiException` with `fieldErrors`.
  Future<ApiResponse<WorkerWorkArea>> saveWorkArea(
    SaveWorkAreaRequest request,
  ) {
    return _apiClient.put<WorkerWorkArea>(
      '/api/workers/me/work-area',
      body: request.toJson(),
      fromJson: (json) => WorkerWorkArea.fromJson(json as Map<String, dynamic>),
    );
  }
}
