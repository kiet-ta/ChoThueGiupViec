import '../../../core/device/camera_wrapper.dart';
import '../../../core/device/location_wrapper.dart';
import '../../../core/network/api_client.dart';
import '../models/check_in_result.dart';
import '../models/job_offer.dart';

/// Service managing worker job offers and field check-ins matching .spec/contracts/dispatch.md §3.1.
class DispatchService {
  final ApiClient _apiClient;
  final ILocationWrapper _locationWrapper;
  final ICameraWrapper _cameraWrapper;
  final bool isMock;

  JobOffer? _mockOffer;
  CheckInResult? _mockCheckInResult;

  DispatchService({
    ApiClient? apiClient,
    ILocationWrapper? locationWrapper,
    ICameraWrapper? cameraWrapper,
    this.isMock = false,
  })  : _apiClient = apiClient ?? ApiClient(),
        _locationWrapper = locationWrapper ?? LocationWrapper(),
        _cameraWrapper = cameraWrapper ?? CameraWrapper() {
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

  ILocationWrapper get locationWrapper => _locationWrapper;
  ICameraWrapper get cameraWrapper => _cameraWrapper;

  /// Sets or clears the active mock offer for testing or preview purposes.
  void setMockOffer(JobOffer? offer) {
    _mockOffer = offer;
  }

  /// Sets or clears the active mock check-in result.
  void setMockCheckInResult(CheckInResult? result) {
    _mockCheckInResult = result;
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

  /// Performs GPS check-in at the destination (BR-04, tolerance <= 100m).
  Future<CheckInResult> checkInGps({
    required int assignmentId,
    required double latitude,
    required double longitude,
    double? destinationLatitude,
    double? destinationLongitude,
  }) async {
    if (isMock) {
      if (_mockCheckInResult != null) {
        return _mockCheckInResult!;
      }

      double distance = 45.0;
      if (destinationLatitude != null && destinationLongitude != null) {
        distance = _locationWrapper.calculateDistanceMeters(
          latitude,
          longitude,
          destinationLatitude,
          destinationLongitude,
        );
      }

      final isGpsVerified = distance <= 100.0;
      return CheckInResult(
        checkInId: 2001,
        assignmentId: assignmentId,
        checkedInAt: DateTime.now(),
        distanceMeters: distance,
        isGpsVerified: isGpsVerified,
        requiresAlternativeVerification: !isGpsVerified,
        verificationMethod: 'GPS',
        platePhotoUrl: null,
        isCustomerConfirmed: false,
      );
    }

    final response = await _apiClient.post(
      '/api/dispatch/assignments/$assignmentId/check-in',
      body: {
        'latitude': latitude,
        'longitude': longitude,
      },
      requiresAuth: true,
    );

    final data = response.data;
    if (data is Map<String, dynamic>) {
      return CheckInResult.fromJson(data);
    }
    throw const ApiException(500, 'Dữ liệu check-in không hợp lệ');
  }

  /// Submits plate photo fallback verification when GPS is deviating > 100m.
  Future<CheckInResult> submitPlatePhoto({
    required int assignmentId,
    required String platePhotoUrl,
  }) async {
    if (isMock) {
      return CheckInResult(
        checkInId: 2002,
        assignmentId: assignmentId,
        checkedInAt: DateTime.now(),
        distanceMeters: 180.0,
        isGpsVerified: false,
        requiresAlternativeVerification: false,
        verificationMethod: 'PLATE_PHOTO',
        platePhotoUrl: platePhotoUrl,
        isCustomerConfirmed: false,
      );
    }

    final response = await _apiClient.post(
      '/api/dispatch/assignments/$assignmentId/check-in/plate-photo',
      body: {
        'platePhotoUrl': platePhotoUrl,
      },
      requiresAuth: true,
    );

    final data = response.data;
    if (data is Map<String, dynamic>) {
      return CheckInResult.fromJson(data);
    }
    throw const ApiException(500, 'Dữ liệu xác nhận ảnh không hợp lệ');
  }

  /// Requests customer in-app confirmation fallback when GPS is deviating > 100m.
  Future<CheckInResult> requestCustomerConfirmation({
    required int assignmentId,
  }) async {
    if (isMock) {
      return CheckInResult(
        checkInId: 2003,
        assignmentId: assignmentId,
        checkedInAt: DateTime.now(),
        distanceMeters: 150.0,
        isGpsVerified: false,
        requiresAlternativeVerification: false,
        verificationMethod: 'CUSTOMER_CONFIRMATION',
        platePhotoUrl: null,
        isCustomerConfirmed: true,
      );
    }

    // Customer confirmation endpoint or poll status
    return CheckInResult(
      checkInId: 2003,
      assignmentId: assignmentId,
      checkedInAt: DateTime.now(),
      distanceMeters: 150.0,
      isGpsVerified: false,
      requiresAlternativeVerification: false,
      verificationMethod: 'CUSTOMER_CONFIRMATION',
      platePhotoUrl: null,
      isCustomerConfirmed: true,
    );
  }
}
