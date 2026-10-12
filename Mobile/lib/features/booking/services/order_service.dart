import '../../../core/network/api_client.dart';
import '../models/booking_models.dart';

/// What the order screens (history, tracking, cancel, "Làm lần 2") need from the server; a test can supply its own.
abstract class IOrderService {
  Future<OrderPage> listOrders({int page = 1, int pageSize = 20});

  Future<BookingOrder> getOrder(int orderId);

  Future<OrderProgress> getProgress(int orderId);

  /// The limits the extension form needs (the shift length).
  Future<BookingOptions> getOptions();

  Future<BookingOrder> cancelOrder(int orderId, String reason);

  Future<OrderExtension> requestExtension({required int orderId, required int assignmentId, required double extraHours});
}

/// booking.md 3.1 and 3.4 to 3.8 over the shared [ApiClient]. Failures are thrown as [ApiException] (with its business `code`).
class OrderService implements IOrderService {
  final ApiClient _client;

  OrderService({ApiClient? client}) : _client = client ?? ApiClient();

  T _require<T>(T? data, String what) {
    if (data == null) throw ApiException(0, 'Máy chủ không trả $what.');
    return data;
  }

  static BookingOrder _order(dynamic json) => BookingOrder.fromJson(json as Map<String, dynamic>);

  @override
  Future<OrderPage> listOrders({int page = 1, int pageSize = 20}) async {
    final response = await _client.get<OrderPage>(
      '/api/booking/orders',
      queryParams: {'page': '$page', 'pageSize': '$pageSize'},
      fromJson: (json) => OrderPage.fromJson(json as Map<String, dynamic>),
    );
    return _require(response.data, 'danh sách đơn');
  }

  @override
  Future<BookingOrder> getOrder(int orderId) async =>
      _require((await _client.get<BookingOrder>('/api/booking/orders/$orderId', fromJson: _order)).data, 'đơn');

  @override
  Future<OrderProgress> getProgress(int orderId) async {
    final response = await _client.get<OrderProgress>(
      '/api/booking/orders/$orderId/progress',
      fromJson: (json) => OrderProgress.fromJson(json as Map<String, dynamic>),
    );
    return _require(response.data, 'tiến độ đơn');
  }

  @override
  Future<BookingOptions> getOptions() async {
    final response = await _client.get<BookingOptions>(
      '/api/booking/options',
      fromJson: (json) => BookingOptions.fromJson(json as Map<String, dynamic>),
    );
    return _require(response.data, 'các lựa chọn đặt đơn');
  }

  @override
  Future<BookingOrder> cancelOrder(int orderId, String reason) async => _require(
        (await _client.post<BookingOrder>('/api/booking/orders/$orderId/cancel', body: {'reason': reason}, fromJson: _order)).data,
        'đơn đã huỷ',
      );

  @override
  Future<OrderExtension> requestExtension({required int orderId, required int assignmentId, required double extraHours}) async {
    final response = await _client.post<OrderExtension>(
      '/api/booking/orders/$orderId/extensions',
      body: {'assignmentId': assignmentId, 'extraHours': extraHours},
      fromJson: (json) => OrderExtension.fromJson(json as Map<String, dynamic>),
    );
    return _require(response.data, 'yêu cầu làm thêm giờ');
  }
}
