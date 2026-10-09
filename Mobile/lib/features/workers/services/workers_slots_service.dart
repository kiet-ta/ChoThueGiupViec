import '../../../core/network/api_client.dart';
import '../../../core/network/api_response.dart';
import '../models/booking_slot_models.dart';

/// Service for worker availability slots API calls.
class WorkersSlotsService {
  final ApiClient _apiClient;

  WorkersSlotsService({ApiClient? apiClient})
      : _apiClient = apiClient ?? ApiClient();

  /// Fetches active availability slots for a date range (contract workers.md §2.3.1).
  Future<ApiResponse<List<BookingSlotDto>>> getSlots({
    required String startDate,
    required String endDate,
  }) {
    return _apiClient.get<List<BookingSlotDto>>(
      '/api/workers/me/slots',
      queryParams: {
        'startDate': startDate,
        'endDate': endDate,
      },
      fromJson: (json) {
        if (json is List) {
          return json
              .map((e) => BookingSlotDto.fromJson(e as Map<String, dynamic>))
              .toList();
        }
        return <BookingSlotDto>[];
      },
    );
  }

  /// Toggles availability status for a slot (contract §2.3.2).
  Future<ApiResponse<BookingSlotDto>> toggleSlot(ToggleBookingSlotRequest request) {
    return _apiClient.post<BookingSlotDto>(
      '/api/workers/me/slots/toggle',
      body: request.toJson(),
      fromJson: (json) => BookingSlotDto.fromJson(json as Map<String, dynamic>),
    );
  }
}
