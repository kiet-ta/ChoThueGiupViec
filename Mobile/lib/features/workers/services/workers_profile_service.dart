import '../../../core/network/api_client.dart';

/// What the Tài khoản tab needs from the profile: the name and whether the worker belongs to an agency.
typedef WorkerSummary = ({String? fullName, bool agencyWorker});

/// Reads the signed-in worker's profile (contract workers.md §2.1.2, `GET /api/workers/me`).
class WorkersProfileService {
  final ApiClient _apiClient;

  WorkersProfileService({ApiClient? apiClient})
    : _apiClient = apiClient ?? ApiClient();

  static String? _nameOf(Object? json) {
    final name = json is Map<String, dynamic> ? json['fullName'] : null;
    return name is String && name.trim().isNotEmpty ? name.trim() : null;
  }

  /// The worker's `fullName`, or null when the profile has none.
  Future<String?> getFullName() async {
    final response = await _apiClient.get<String?>(
      '/api/workers/me',
      fromJson: _nameOf,
    );
    return response.data;
  }

  /// The name and the agency flag (`agencyId` is set for an agency worker) from one read of the profile.
  /// A call that fails throws, like [getFullName].
  Future<WorkerSummary> getSummary() async {
    final response = await _apiClient.get<WorkerSummary>(
      '/api/workers/me',
      fromJson: (json) => (
        fullName: _nameOf(json),
        agencyWorker: json is Map<String, dynamic> && json['agencyId'] != null,
      ),
    );
    return response.data ?? (fullName: null, agencyWorker: false);
  }
}
