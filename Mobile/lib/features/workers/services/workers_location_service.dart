import '../../../core/device/location_wrapper.dart';
import '../../../core/network/api_client.dart';
import '../../../core/network/api_response.dart';
import '../models/worker_location_models.dart';

/// Tells the server where the worker is (contract workers.md §2.3A, decisions D19). The dispatch search offers a job only to a worker who
/// has a position, so the availability screen calls this when it opens and when the worker switches a slot ON.
class WorkersLocationService {
  final ApiClient _apiClient;
  final ILocationWrapper _locationWrapper;

  WorkersLocationService({
    ApiClient? apiClient,
    ILocationWrapper? locationWrapper,
  }) : _apiClient = apiClient ?? ApiClient(),
       _locationWrapper = locationWrapper ?? LocationWrapper();

  /// Sends the position of the device (`PUT /api/workers/me/location`).
  Future<ApiResponse<WorkerLocationDto>> shareCurrentLocation() async {
    final point = await _locationWrapper.getCurrentLocation();
    return updateLocation(latitude: point.latitude, longitude: point.longitude);
  }

  Future<ApiResponse<WorkerLocationDto>> updateLocation({
    required double latitude,
    required double longitude,
  }) {
    return _apiClient.put<WorkerLocationDto>(
      '/api/workers/me/location',
      body: {'latitude': latitude, 'longitude': longitude},
      fromJson: (json) =>
          WorkerLocationDto.fromJson(json as Map<String, dynamic>),
    );
  }
}
