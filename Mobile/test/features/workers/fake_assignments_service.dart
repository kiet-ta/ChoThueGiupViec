import 'package:mobile/features/workers/logic/acceptance_logic.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/network/api_response.dart';
import 'package:mobile/features/workers/models/worker_assignment_models.dart';
import 'package:mobile/features/workers/services/workers_assignments_service.dart';

/// Answers `GET /api/v1/workers/assignments/{id}` with a job in [status], or fails with [failure].
class FakeAssignmentsService extends WorkersAssignmentsService {
  String status;
  ApiException? failure;
  int calls = 0;

  /// ACCEPT-02: the acceptance block the server would send (null by default, like before the work).
  AcceptanceBlock? acceptance;

  FakeAssignmentsService({
    this.status = 'IN_PROGRESS',
    this.failure,
    this.acceptance,
  }) : super(apiClient: ApiClient(baseUrl: 'http://api.test'));

  @override
  Future<ApiResponse<WorkerAssignmentDetailDto>> getAssignment(
    int assignmentId,
  ) async {
    calls++;
    if (failure != null) throw failure!;
    return ApiResponse<WorkerAssignmentDetailDto>(
      success: true,
      message: 'OK',
      data: WorkerAssignmentDetailDto(
        assignmentId: assignmentId,
        orderId: 1,
        orderCode: 'ORD-1',
        slotDate: '2026-11-07',
        shiftCode: 'SHIFT_MORNING',
        startTime: '08:00',
        endTime: '12:00',
        status: status,
        serviceTier: 'STANDARD',
        addressLine: '1 Le Loi',
        district: 'Quan 1',
        city: 'TP.HCM',
        latitude: 10.77,
        longitude: 106.7,
        payoutAmount: 400000,
        acceptance: acceptance,
      ),
    );
  }
}
