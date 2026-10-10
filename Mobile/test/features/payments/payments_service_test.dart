import 'dart:convert';
import 'dart:io';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/auth/token_storage.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/payments/models/payment_models.dart';
import 'package:mobile/features/payments/services/payments_service.dart';

const _payment = {
  'paymentId': 5,
  'purpose': 'ORDER',
  'orderId': 42,
  'extensionId': null,
  'gateway': 'FAKE',
  'amount': 260000,
  'txnStatus': 'PENDING',
  'qrPayload': 'fake-qr://FAKE-1',
  'payUrl': 'https://sandbox.invalid/pay/FAKE-1',
  'expiresAt': '2026-10-14T03:15:00Z',
  'paidAt': null,
  'createdAt': '2026-10-14T03:00:00Z',
  'sandbox': true,
};

void main() {
  group('PaymentsService over a local server', () {
    late HttpServer server;
    late PaymentsService service;
    final seen = <({String method, String path, String body})>[];

    Future<void> answer(int status, Object? json) async {
      server.listen((HttpRequest request) async {
        final body = await utf8.decodeStream(request);
        seen.add((method: request.method, path: request.uri.path, body: body));
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
      service = PaymentsService(client: ApiClient(baseUrl: 'http://${server.address.host}:${server.port}'));
    });

    tearDown(() async {
      await server.close(force: true);
      TokenStorage().clear();
    });

    test('creates the QR of an order with a POST without a body', () async {
      await answer(201, {'success': true, 'message': 'Payment QR created.', 'data': _payment});

      final payment = await service.createOrderQr(42);

      expect(seen.single.method, 'POST');
      expect(seen.single.path, '/api/payments/orders/42/qr');
      expect(seen.single.body, isEmpty);
      expect(payment.paymentId, 5);
      expect(payment.amount, 260000);
      expect(payment.txnStatus, PaymentTxnStatus.pending);
      expect(payment.qrPayload, 'fake-qr://FAKE-1');
      expect(payment.payUrl, 'https://sandbox.invalid/pay/FAKE-1');
      expect(payment.expiresAt, DateTime.utc(2026, 10, 14, 3, 15));
      expect(payment.sandbox, isTrue);
      expect(payment.isPending, isTrue);
    });

    test('an existing QR (200) is read the same way', () async {
      await answer(200, {'success': true, 'message': 'Payment QR already exists.', 'data': _payment});

      expect((await service.createOrderQr(42)).paymentId, 5);
    });

    test('creates the QR of an extension on its own path', () async {
      await answer(201, {
        'success': true,
        'message': '',
        'data': {..._payment, 'purpose': 'EXTENSION', 'orderId': null, 'extensionId': 9, 'amount': 97500},
      });

      final payment = await service.createExtensionQr(9);

      expect(seen.single.path, '/api/payments/extensions/9/qr');
      expect(payment.purpose, 'EXTENSION');
      expect(payment.extensionId, 9);
      expect(payment.orderId, isNull);
      expect(payment.amount, 97500);
    });

    test('reads a payment for the polling fallback', () async {
      await answer(200, {
        'success': true,
        'message': '',
        'data': {..._payment, 'txnStatus': 'SUCCESS', 'qrPayload': null, 'payUrl': null, 'paidAt': '2026-10-14T03:02:00Z'},
      });

      final payment = await service.getPayment(5);

      expect(seen.single.method, 'GET');
      expect(seen.single.path, '/api/payments/5');
      expect(payment.isPaid, isTrue);
      expect(payment.qrPayload, isNull);
      expect(payment.paidAt, DateTime.utc(2026, 10, 14, 3, 2));
    });

    test('a refused QR carries its business code', () async {
      await answer(409, {
        'success': false,
        'message': 'The payment deadline of the order has passed.',
        'data': {'code': 'PAYMENT_EXPIRED'},
      });

      await expectLater(
        service.createOrderQr(42),
        throwsA(isA<ApiException>().having((e) => e.statusCode, 'status', 409).having((e) => e.code, 'code', 'PAYMENT_EXPIRED')),
      );
    });

    test('a gateway failure is a 502', () async {
      await answer(502, {'success': false, 'message': 'The payment gateway did not create the QR.', 'data': null});

      await expectLater(service.createOrderQr(42), throwsA(isA<ApiException>().having((e) => e.statusCode, 'status', 502)));
    });
  });
}
