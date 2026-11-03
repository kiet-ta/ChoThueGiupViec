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
      '/api/v1/workers/me/slots',
      queryParams: {'startDate': startDate, 'endDate': endDate},
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

  /// Sets a slot on or off (`PUT /api/v1/workers/me/slots/{date}/{shiftCode}`, contract §2.3.2); the server creates the slot when it
  /// does not exist yet.
  Future<ApiResponse<BookingSlotDto>> toggleSlot(
    ToggleBookingSlotRequest request,
  ) {
    return _apiClient.put<BookingSlotDto>(
      '/api/v1/workers/me/slots/${request.slotDate}/${request.shiftCode}',
      body: {'enabled': request.isActive},
      fromJson: (json) => BookingSlotDto.fromJson(json as Map<String, dynamic>),
    );
  }
}
