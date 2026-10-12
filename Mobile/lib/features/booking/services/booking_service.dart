import '../../../core/network/api_client.dart';
import '../../customers/models/customer_address.dart';
import '../../customers/services/customer_address_service.dart';
import '../../identity/services/api_error.dart' as identity;
import '../models/booking_models.dart';

/// What the booking screens need from the server; a test or a preview can supply its own.
abstract class IBookingService {
  Future<BookingOptions> getOptions();

  Future<PriceQuote> getQuote({required int addressId, required String serviceTier});

  Future<BookingOrder> createOrder(CreateOrderInput input);
}

/// The customer's addresses, read through the Customers feature (its address book is the only owner of that data).
abstract class IBookingAddressSource {
  Future<List<BookingAddress>> getAddresses();
}

/// booking.md 3.1 to 3.3 over the shared [ApiClient]. Failures are thrown as [ApiException] (with its business `code`).
class BookingService implements IBookingService {
  final ApiClient _client;

  BookingService({ApiClient? client}) : _client = client ?? ApiClient();

  T _require<T>(T? data, String what) {
    if (data == null) throw ApiException(0, 'Máy chủ không trả $what.');
    return data;
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
  Future<PriceQuote> getQuote({required int addressId, required String serviceTier}) async {
    final response = await _client.get<PriceQuote>(
      '/api/booking/price-quote',
      queryParams: {'addressId': '$addressId', 'serviceTier': serviceTier},
      fromJson: (json) => PriceQuote.fromJson(json as Map<String, dynamic>),
    );
    return _require(response.data, 'báo giá');
  }

  @override
  Future<BookingOrder> createOrder(CreateOrderInput input) async {
    final response = await _client.post<BookingOrder>(
      '/api/booking/orders',
      body: input.toJson(),
      fromJson: (json) => BookingOrder.fromJson(json as Map<String, dynamic>),
    );
    return _require(response.data, 'đơn vừa tạo');
  }
}

/// Default [IBookingAddressSource]: the Customers feature's own service (`GET /api/customers/me/addresses`).
class CustomerAddressBookSource implements IBookingAddressSource {
  final CustomerAddressService _addresses;

  CustomerAddressBookSource({CustomerAddressService? addresses}) : _addresses = addresses ?? CustomerAddressService();

  @override
  Future<List<BookingAddress>> getAddresses() async {
    final List<CustomerAddress> list;
    try {
      list = await _addresses.getAddresses();
    } on identity.ApiException catch (e) {
      // The Customers service has its own exception type; the booking screens handle the shared one.
      throw ApiException(e.statusCode, e.message, fieldErrors: e.fieldErrors, retryAfterSeconds: e.retryAfterSeconds);
    }

    return [
      for (final a in list)
        BookingAddress(
          addressId: a.addressId,
          label: a.label,
          addressLine: a.addressLine,
          totalAreaM2: a.totalAreaM2,
          isDefault: a.isDefault,
        ),
    ];
  }
}
