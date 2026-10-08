import '../../../core/network/api_client.dart';
import '../models/job_offer.dart';

/// Service managing worker job offers and responses matching .spec/contracts/dispatch.md §3.1.
class DispatchService {
  final ApiClient _apiClient;
  final bool isMock;

  JobOffer? _mockOffer;

  DispatchService({
    ApiClient? apiClient,
    this.isMock = false,
  }) : _apiClient = apiClient ?? ApiClient() {
    if (isMock) {
      _mockOffer = JobOffer(
        assignmentId: 1001,
        orderId: 501,
        serviceTier: 'ECONOMY',
        bookingDate: '2026-10-15',
        shiftTime: '08:00 - 12:00',
        district: 'Quận Cầu Giấy',
        approximateDistanceKm: 3.4,
        estimatedDurationHours: 4.0,
        grossAmount: 260000.0,
        commissionRate: 0.20,
        netEarnings: 208000.0,
        offeredAt: DateTime.now(),
        expiresAt: DateTime.now().add(const Duration(seconds: 30)),
        remainingSeconds: 30,
      );
    }
  }

  /// Sets or clears the active mock offer for testing or preview purposes.
  void setMockOffer(JobOffer? offer) {
    _mockOffer = offer;
  }

  /// Fetches the currently pending job offer for the worker.
  /// Returns null if there are no pending offers.
  Future<JobOffer?> getCurrentOffer() async {
    if (isMock) {
      return _mockOffer;
    }

    try {
      final response = await _apiClient.get(
        '/api/dispatch/offers/current',
        requiresAuth: true,
      );

      final data = response.data;
      if (data == null || data is! Map<String, dynamic>) {
        return null;
      }
      return JobOffer.fromJson(data);
    } on ApiException catch (e) {
      if (e.statusCode == 404) {
        return null;
      }
      rethrow;
    }
  }

  /// Accepts the job offer within the 30-second window.
  Future<AcceptOfferResult> acceptOffer(int assignmentId) async {
    if (isMock) {
      if (_mockOffer == null || _mockOffer!.assignmentId != assignmentId) {
        throw const ApiException(404, 'Không tìm thấy ca làm việc');
      }
      final result = AcceptOfferResult(
        assignmentId: assignmentId,
        status: 'ASSIGNED',
        acceptedAt: DateTime.now(),
        bookingSlotLocked: true,
      );
      _mockOffer = null;
      return result;
    }

    final response = await _apiClient.post(
      '/api/dispatch/offers/$assignmentId/accept',
      requiresAuth: true,
    );

    final data = response.data;
    if (data is Map<String, dynamic>) {
      return AcceptOfferResult.fromJson(data);
    }
    return AcceptOfferResult(
      assignmentId: assignmentId,
      status: 'ASSIGNED',
      acceptedAt: DateTime.now(),
      bookingSlotLocked: true,
    );
  }

  /// Declines the job offer.
  Future<void> declineOffer(int assignmentId, {String? reason}) async {
    if (isMock) {
      _mockOffer = null;
      return;
    }

    await _apiClient.post(
      '/api/dispatch/offers/$assignmentId/decline',
      body: {'reason': reason ?? 'Bận việc đột xuất'},
      requiresAuth: true,
    );
  }
}
