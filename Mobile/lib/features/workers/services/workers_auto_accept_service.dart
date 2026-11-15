import '../../../core/network/api_client.dart';
import '../../../core/network/api_response.dart';

/// The freelancer's "Tự động nhận đơn" switch (contract workers.md §2.3C, decisions D33,
/// `GET` / `PUT /api/v1/workers/me/auto-accept`). While it is on, the server accepts each new job offer for the worker.
class WorkersAutoAcceptService {
  /// 409 `data.code` when the worker has no registered work area yet.
  static const workAreaRequired = 'WORK_AREA_REQUIRED';

  /// 409 `data.code` for an agency worker (they never get offers).
  static const notFreelancer = 'NOT_FREELANCER';

  final ApiClient _apiClient;

  WorkersAutoAcceptService({ApiClient? apiClient})
    : _apiClient = apiClient ?? ApiClient();

  /// The stored value (off by default).
  Future<bool> isEnabled() async {
    final response = await _apiClient.get<bool>(
      '/api/v1/workers/me/auto-accept',
      fromJson: _enabled,
    );
    return response.data ?? false;
  }

  /// Turns it on or off and returns the value the server kept. A refusal comes back as an `ApiException` with a [code].
  Future<bool> setEnabled(bool enabled) async {
    final ApiResponse<bool> response = await _apiClient.put<bool>(
      '/api/v1/workers/me/auto-accept',
      body: {'enabled': enabled},
      fromJson: _enabled,
    );
    return response.data ?? false;
  }

  static bool _enabled(dynamic json) =>
      json is Map<String, dynamic> && json['enabled'] == true;
}
