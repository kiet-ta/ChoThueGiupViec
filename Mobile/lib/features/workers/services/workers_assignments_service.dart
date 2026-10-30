import '../../../core/network/api_client.dart';
import '../../../core/network/api_response.dart';
import '../models/worker_assignment_models.dart';

/// Reads the jobs booked for the signed-in worker (contract workers.md §2.3.3, `GET /api/workers/me/assignments`).
class WorkersAssignmentsService {
  final ApiClient _apiClient;

  WorkersAssignmentsService({ApiClient? apiClient})
    : _apiClient = apiClient ?? ApiClient();

  /// The booked jobs whose slot date is in [startDate, endDate] (yyyy-MM-dd), by date and shift.
  Future<ApiResponse<List<WorkerAssignmentDto>>> getAssignments({
    required String startDate,
    required String endDate,
  }) {
    return _apiClient.get<List<WorkerAssignmentDto>>(
      '/api/workers/me/assignments',
      queryParams: {'startDate': startDate, 'endDate': endDate},
      fromJson: (json) {
        if (json is List) {
          return json
              .map(
                (e) => WorkerAssignmentDto.fromJson(e as Map<String, dynamic>),
              )
              .toList();
        }
        return <WorkerAssignmentDto>[];
      },
    );
  }
}
