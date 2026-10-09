import '../../../core/network/api_client.dart';
import '../../../core/network/api_response.dart';
import '../models/worker_extension_models.dart';

/// Service for worker extension responses ("Làm lần 2", BR-08, contract workers.md §2.6).
class WorkerExtensionService {
  final ApiClient _apiClient;

  WorkerExtensionService({ApiClient? apiClient})
      : _apiClient = apiClient ?? ApiClient();

  /// Worker responds to extension request paid by customer (contract workers.md §2.6.1).
  ///
  /// Sends `POST /api/workers/assignments/{assignmentId}/extension/respond` with `{ accept: bool }`.
  Future<ApiResponse<WorkerExtensionResponseDto>> respondExtension({
    required int assignmentId,
    required bool accept,
  }) {
    return _apiClient.post<WorkerExtensionResponseDto>(
      '/api/workers/assignments/$assignmentId/extension/respond',
      body: {'accept': accept},
      fromJson: (json) => WorkerExtensionResponseDto.fromJson(json as Map<String, dynamic>),
    );
  }
}
