import '../../../core/network/api_client.dart';
import '../models/payment_models.dart';

/// What the payment screen needs from the server; a test or a preview can supply its own.
abstract class IPaymentsService {
  /// POST /api/payments/orders/{orderId}/qr: creates the QR, or returns the PENDING one that already exists.
  Future<Payment> createOrderQr(int orderId);

  /// POST /api/payments/extensions/{extensionId}/qr.
  Future<Payment> createExtensionQr(int extensionId);

  /// GET /api/payments/{paymentId}: the polling fallback of the realtime `payment.status` push.
  Future<Payment> getPayment(int paymentId);
}

/// payments.md 2.1 to 2.3 over the shared [ApiClient]. Failures are thrown as [ApiException] (with its business `code`).
class PaymentsService implements IPaymentsService {
  final ApiClient _client;

  PaymentsService({ApiClient? client}) : _client = client ?? ApiClient();

  Payment _require(Payment? data) {
    if (data == null) throw const ApiException(0, 'Máy chủ không trả thông tin thanh toán.');
    return data;
  }

  static Payment _parse(dynamic json) => Payment.fromJson(json as Map<String, dynamic>);

  @override
  Future<Payment> createOrderQr(int orderId) async =>
      _require((await _client.post<Payment>('/api/payments/orders/$orderId/qr', fromJson: _parse)).data);

  @override
  Future<Payment> createExtensionQr(int extensionId) async =>
      _require((await _client.post<Payment>('/api/payments/extensions/$extensionId/qr', fromJson: _parse)).data);

  @override
  Future<Payment> getPayment(int paymentId) async =>
      _require((await _client.get<Payment>('/api/payments/$paymentId', fromJson: _parse)).data);
}
