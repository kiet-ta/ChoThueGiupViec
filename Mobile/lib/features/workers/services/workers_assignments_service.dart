import '../../../core/network/api_client.dart';
import '../../../core/network/api_response.dart';
import '../models/worker_assignment_models.dart';

/// Reads the jobs booked for the signed-in worker (contract workers.md §2.3.3, `GET /api/v1/workers/me/assignments`).
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
      '/api/v1/workers/me/assignments',
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

  /// One of the worker's own jobs with the full address (contract workers.md §2.3.4). 404 for an unknown job, another worker's job and an offer.
  Future<ApiResponse<WorkerAssignmentDetailDto>> getAssignment(
    int assignmentId,
  ) {
    return _apiClient.get<WorkerAssignmentDetailDto>(
      '/api/v1/workers/assignments/$assignmentId',
      fromJson: (json) =>
          WorkerAssignmentDetailDto.fromJson(json as Map<String, dynamic>),
    );
  }
}
