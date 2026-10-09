import '../../../core/network/api_client.dart';
import '../../../core/network/api_response.dart';
import '../models/customer_acceptance_models.dart';

/// Service for customer job acceptance & rework requests (contract workers.md §2.5).
class CustomerAcceptanceService {
  final ApiClient _apiClient;

  CustomerAcceptanceService({ApiClient? apiClient})
      : _apiClient = apiClient ?? ApiClient();

  /// Customer confirms job completion (contract workers.md §2.5.2).
  Future<ApiResponse<AssignmentCompletionDto>> acceptCompletion({
    required int assignmentId,
    String? feedback,
  }) {
    return _apiClient.post<AssignmentCompletionDto>(
      '/api/customers/assignments/$assignmentId/accept',
      body: feedback != null && feedback.isNotEmpty ? {'feedback': feedback} : {},
      fromJson: (json) => AssignmentCompletionDto.fromJson(json as Map<String, dynamic>),
    );
  }

  /// Customer requests a 15-30 minute touch-up/redo (contract workers.md §2.5.3).
  Future<ApiResponse<AssignmentStatusDto>> requestRedo({
    required int assignmentId,
    required RequestRedoRequest request,
  }) {
    return _apiClient.post<AssignmentStatusDto>(
      '/api/customers/assignments/$assignmentId/request-redo',
      body: request.toJson(),
      fromJson: (json) => AssignmentStatusDto.fromJson(json as Map<String, dynamic>),
    );
  }
}
