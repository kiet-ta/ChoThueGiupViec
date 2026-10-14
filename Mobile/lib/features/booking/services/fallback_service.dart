import '../../../core/network/api_client.dart';
import '../models/fallback_models.dart';

/// What the "only one worker" question needs from the server; a test can supply its own.
abstract class IFallbackService {
  Future<FallbackStatus> getStatus(int orderId);

  /// `accept` true: the worker is asked to confirm the second consecutive shift; false: the order is cancelled and refunded.
  Future<FallbackResponse> respond(int orderId, {required bool accept});
}

/// dispatch.md 3.2 over the shared [ApiClient]. Failures are thrown as [ApiException] (with its business `code`).
class FallbackService implements IFallbackService {
  final ApiClient _client;

  FallbackService({ApiClient? client}) : _client = client ?? ApiClient();

  T _require<T>(T? data, String what) {
    if (data == null) throw ApiException(0, 'Máy chủ không trả $what.');
    return data;
  }

  @override
  Future<FallbackStatus> getStatus(int orderId) async {
    final response = await _client.get<FallbackStatus>(
      '/api/dispatch/orders/$orderId/fallback',
      fromJson: (json) => FallbackStatus.fromJson(json as Map<String, dynamic>),
    );
    return _require(response.data, 'trạng thái chọn thợ');
  }

  @override
  Future<FallbackResponse> respond(int orderId, {required bool accept}) async {
    final response = await _client.post<FallbackResponse>(
      '/api/dispatch/orders/$orderId/fallback/respond',
      body: {'accept': accept},
      fromJson: (json) =>
          FallbackResponse.fromJson(json as Map<String, dynamic>),
    );
    return _require(response.data, 'kết quả lựa chọn');
  }
}
