import 'dart:convert';
import 'dart:io';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/auth/token_storage.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/booking/models/booking_models.dart';
import 'package:mobile/features/booking/services/booking_service.dart';

void main() {
  group('BookingService over a local server', () {
    late HttpServer server;
    late BookingService service;
    final seen = <({String method, String path, Map<String, String> query, String body, String? auth})>[];

    Future<void> answer(int status, Object? json) async {
      server.listen((HttpRequest request) async {
        final body = await utf8.decodeStream(request);
        seen.add((
          method: request.method,
          path: request.uri.path,
          query: request.uri.queryParameters,
          body: body,
          auth: request.headers.value('authorization'),
        ));
        request.response.statusCode = status;
        request.response.headers.contentType = ContentType.json;
        request.response.write(jsonEncode(json));
        await request.response.close();
      });
    }

    setUp(() async {
      seen.clear();
      TokenStorage().clear();
      TokenStorage().saveTokens(accessToken: 'tok', refreshToken: 'ref');
      server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
      service = BookingService(client: ApiClient(baseUrl: 'http://${server.address.host}:${server.port}'));
    });

    tearDown(() async {
      await server.close(force: true);
      TokenStorage().clear();
    });

    test('reads the booking options', () async {
      await answer(200, {
        'success': true,
        'message': '',
        'data': {
          'serviceTiers': ['ECONOMY', 'PREMIUM'],
          'shifts': [
            {'shiftCode': 'SHIFT_MORNING', 'startLocal': '08:00', 'endLocal': '12:00'},
          ],
          'premiumMinLeadHours': 4,
          'shiftMaxHours': 4,
          'standardMaxAreaM2': 80,
          'paymentQrExpiryMinutes': 15,
          'sandbox': true,
        },
      });

      final options = await service.getOptions();

      expect(seen.single.method, 'GET');
      expect(seen.single.path, '/api/booking/options');
      expect(seen.single.auth, 'Bearer tok');
      expect(options.serviceTiers, ['ECONOMY', 'PREMIUM']);
      expect(options.shifts.single.shiftCode, 'SHIFT_MORNING');
      expect(options.shifts.single.endLocal, '12:00');
      expect(options.premiumMinLeadHours, 4);
      expect(options.standardMaxAreaM2, 80);
      expect(options.paymentQrExpiryMinutes, 15);
      expect(options.sandbox, isTrue);
    });

    test('asks the price quote with the address and the tier in the query', () async {
      await answer(200, {
        'success': true,
        'message': '',
        'data': {
          'addressId': 3,
          'serviceTier': 'PREMIUM',
          'totalAreaM2': 90.5,
          'areaBracket': 'OVER_80',
          'requiredWorkers': 2,
          'unitPrice': 390000,
          'totalAmount': 780000,
          'currency': 'VND',
        },
      });

      final quote = await service.getQuote(addressId: 3, serviceTier: ServiceTier.premium);

      expect(seen.single.path, '/api/booking/price-quote');
      expect(seen.single.query, {'addressId': '3', 'serviceTier': 'PREMIUM'});
      expect(quote.requiredWorkers, 2);
      expect(quote.unitPrice, 390000);
      expect(quote.totalAmount, 780000);
      expect(quote.totalAreaM2, 90.5);
      expect(quote.areaBracket, 'OVER_80');
    });

    test('creates an order with the body of the contract and reads the order back', () async {
      await answer(201, {
        'success': true,
        'message': 'Order created.',
        'data': {
          'orderId': 100,
          'orderCode': 'GV261014ABC123',
          'serviceTier': 'PREMIUM',
          'addressId': 3,
          'scheduledDate': '2026-10-15',
          'shiftCode': 'SHIFT_EVENING',
          'shiftStartAt': '2026-10-15T10:30:00Z',
          'shiftEndAt': '2026-10-15T13:30:00Z',
          'areaSnapshotM2': 90.5,
          'requiredWorkers': 2,
          'requiredSkill': 'deep clean',
          'totalAmount': 780000,
          'orderStatus': 'PENDING_PAYMENT',
          'customerNote': 'ring the bell',
          'cancelReason': null,
          'paymentDeadlineAt': '2026-10-14T00:15:00Z',
          'createdAt': '2026-10-14T00:00:00Z',
          'updatedAt': '2026-10-14T00:00:00Z',
        },
      });

      final order = await service.createOrder(const CreateOrderInput(
        addressId: 3,
        serviceTier: ServiceTier.premium,
        scheduledDate: '2026-10-15',
        shiftCode: 'SHIFT_EVENING',
        customerNote: 'ring the bell',
        requiredSkill: 'deep clean',
      ));

      expect(seen.single.method, 'POST');
      expect(seen.single.path, '/api/booking/orders');
      expect(jsonDecode(seen.single.body), {
        'addressId': 3,
        'serviceTier': 'PREMIUM',
        'scheduledDate': '2026-10-15',
        'shiftCode': 'SHIFT_EVENING',
        'customerNote': 'ring the bell',
        'requiredSkill': 'deep clean',
      });
      expect(order.orderId, 100);
      expect(order.orderCode, 'GV261014ABC123');
      expect(order.orderStatus, OrderStatus.pendingPayment);
      expect(order.totalAmount, 780000);
      expect(order.requiredWorkers, 2);
      expect(order.shiftStartAt, DateTime.utc(2026, 10, 15, 10, 30));
      expect(order.paymentDeadlineAt, DateTime.utc(2026, 10, 14, 0, 15));
    });

    test('a refused order carries its business code', () async {
      await answer(409, {
        'success': false,
        'message': 'No partner agency has capacity for this shift.',
        'data': {'code': 'FULLY_BOOKED'},
      });

      await expectLater(
        service.createOrder(const CreateOrderInput(addressId: 3, serviceTier: 'PREMIUM', scheduledDate: '2026-10-15', shiftCode: 'SHIFT_MORNING')),
        throwsA(isA<ApiException>().having((e) => e.statusCode, 'status', 409).having((e) => e.code, 'code', 'FULLY_BOOKED')),
      );
    });

    test('a 400 carries its field errors', () async {
      await answer(400, {
        'success': false,
        'message': 'Validation failed',
        'data': {
          'errors': {
            'scheduledDate': ['scheduledDate must be at most 14 days ahead.'],
          },
        },
      });

      await expectLater(
        service.createOrder(const CreateOrderInput(addressId: 3, serviceTier: 'ECONOMY', scheduledDate: '2027-01-01', shiftCode: 'SHIFT_MORNING')),
        throwsA(isA<ApiException>().having((e) => e.fieldErrors?['scheduledDate']?.first, 'field', contains('14 days'))),
      );
    });

    test('an answer without data is a failure, not an empty object', () async {
      await answer(200, {'success': true, 'message': '', 'data': null});

      await expectLater(service.getOptions(), throwsA(isA<ApiException>()));
    });
  });
}
