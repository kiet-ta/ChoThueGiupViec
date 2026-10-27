import '../../../core/network/api_client.dart';

/// Reads the signed-in worker's profile (contract workers.md §2.1.2, `GET /api/workers/me`).
class WorkersProfileService {
  final ApiClient _apiClient;

  WorkersProfileService({ApiClient? apiClient})
    : _apiClient = apiClient ?? ApiClient();

  /// The worker's `fullName`, or null when the profile has none.
  Future<String?> getFullName() async {
    final response = await _apiClient.get<String?>(
      '/api/workers/me',
      fromJson: (json) {
        final name = json is Map<String, dynamic> ? json['fullName'] : null;
        return name is String && name.trim().isNotEmpty ? name.trim() : null;
      },
    );
    return response.data;
  }
}
