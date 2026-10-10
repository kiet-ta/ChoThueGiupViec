import 'dart:convert';
import 'dart:io';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/auth/token_storage.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/booking/models/booking_models.dart';
import 'package:mobile/features/booking/services/order_service.dart';

Map<String, dynamic> _orderJson(String status, {String? cancelReason}) => {
      'orderId': 100,
      'orderCode': 'GV261014ABC123',
      'serviceTier': 'ECONOMY',
      'addressId': 3,
      'scheduledDate': '2026-10-15',
      'shiftCode': 'SHIFT_MORNING',
      'shiftStartAt': '2026-10-15T01:00:00Z',
      'shiftEndAt': '2026-10-15T05:00:00Z',
      'areaSnapshotM2': 50,
      'requiredWorkers': 1,
      'requiredSkill': null,
      'totalAmount': 260000,
      'orderStatus': status,
      'customerNote': null,
      'cancelReason': cancelReason,
      'paymentDeadlineAt': null,
      'createdAt': '2026-10-14T00:00:00Z',
    };

void main() {
  group('OrderService over a local server', () {
    late HttpServer server;
    late OrderService service;
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
      service = OrderService(client: ApiClient(baseUrl: 'http://${server.address.host}:${server.port}'));
    });

    tearDown(() async {
      await server.close(force: true);
      TokenStorage().clear();
    });

    test('lists a page of orders with page and pageSize in the query', () async {
      await answer(200, {
        'success': true,
        'message': '',
        'data': {
          'items': [
            {
              'orderId': 100,
              'orderCode': 'GV261014ABC123',
              'serviceTier': 'PREMIUM',
              'scheduledDate': '2026-10-15',
              'shiftCode': 'SHIFT_EVENING',
              'requiredWorkers': 2,
              'totalAmount': 780000,
              'orderStatus': 'ASSIGNED',
              'createdAt': '2026-10-14T00:00:00Z',
            },
          ],
          'page': 2,
          'pageSize': 20,
          'total': 21,
        },
      });

      final page = await service.listOrders(page: 2, pageSize: 20);

      expect(seen.single.method, 'GET');
      expect(seen.single.path, '/api/booking/orders');
      expect(seen.single.query, {'page': '2', 'pageSize': '20'});
      expect(seen.single.auth, 'Bearer tok');
      expect(page.total, 21);
      expect(page.page, 2);
      expect(page.items.single.orderCode, 'GV261014ABC123');
      expect(page.items.single.orderStatus, OrderStatus.assigned);
      expect(page.items.single.totalAmount, 780000);
      expect(page.items.single.createdAt, DateTime.utc(2026, 10, 14));
    });

    test('reads one order', () async {
      await answer(200, {'success': true, 'message': '', 'data': _orderJson('DISPATCHING')});

      final order = await service.getOrder(100);

      expect(seen.single.path, '/api/booking/orders/100');
      expect(order.orderStatus, OrderStatus.dispatching);
      expect(order.scheduledDate, '2026-10-15');
    });

    test('reads the progress with its assignments and extension', () async {
      await answer(200, {
        'success': true,
        'message': '',
        'data': {
          'orderId': 100,
          'orderStatus': 'ASSIGNED',
          'requiredWorkers': 1,
          'assignments': [
            {
              'assignmentId': 7,
              'assignmentSeq': 1,
              'workerId': 55,
              'workerName': 'Nguyễn Thị Lan',
              'workerRatingAvg': 4.8,
              'assignmentStatus': 'IN_PROGRESS',
              'acceptedAt': '2026-10-14T02:00:00Z',
              'completedAt': null,
            },
          ],
          'extension': {
            'extensionId': 9,
            'orderId': 100,
            'workerId': 55,
            'extraHours': 1.5,
            'extraAmount': 97500,
            'extStatus': 'PENDING_PAYMENT',
            'workerDecision': 'PENDING',
            'requestedAt': '2026-10-15T04:00:00Z',
            'decidedAt': null,
          },
        },
      });

      final progress = await service.getProgress(100);

      expect(seen.single.path, '/api/booking/orders/100/progress');
      final a = progress.assignments.single;
      expect(a.assignmentId, 7);
      expect(a.workerName, 'Nguyễn Thị Lan');
      expect(a.workerRatingAvg, 4.8);
      expect(a.assignmentStatus, AssignmentStatus.inProgress);
      expect(a.acceptedAt, DateTime.utc(2026, 10, 14, 2));
      expect(progress.extension!.extensionId, 9);
      expect(progress.extension!.extraHours, 1.5);
      expect(progress.extension!.extraAmount, 97500);
      expect(progress.extension!.extStatus, ExtensionStatus.pendingPayment);
    });

    test('a progress without assignments or extension is empty, not a failure', () async {
      await answer(200, {
        'success': true,
        'message': '',
        'data': {'orderId': 100, 'orderStatus': 'DISPATCHING', 'requiredWorkers': 1, 'assignments': [], 'extension': null},
      });

      final progress = await service.getProgress(100);

      expect(progress.assignments, isEmpty);
      expect(progress.extension, isNull);
    });

    test('cancels with the reason in the body and reads the cancelled order back', () async {
      await answer(200, {'success': true, 'message': 'Order cancelled.', 'data': _orderJson('CANCELLED', cancelReason: 'bận việc')});

      final order = await service.cancelOrder(100, 'bận việc');

      expect(seen.single.method, 'POST');
      expect(seen.single.path, '/api/booking/orders/100/cancel');
      expect(jsonDecode(seen.single.body), {'reason': 'bận việc'});
      expect(order.orderStatus, OrderStatus.cancelled);
      expect(order.cancelReason, 'bận việc');
    });

    test('a refused cancel carries its business code', () async {
      await answer(409, {
        'success': false,
        'message': 'The order can no longer be cancelled.',
        'data': {'code': 'INVALID_STATE'},
      });

      await expectLater(
        service.cancelOrder(100, 'x'),
        throwsA(isA<ApiException>().having((e) => e.statusCode, 'status', 409).having((e) => e.code, 'code', 'INVALID_STATE')),
      );
    });

    test('requests an extension with the assignment and the hours and reads it back', () async {
      await answer(201, {
        'success': true,
        'message': '',
        'data': {
          'extensionId': 9,
          'orderId': 100,
          'workerId': 55,
          'extraHours': 1.5,
          'extraAmount': 97500,
          'extStatus': 'PENDING_PAYMENT',
          'workerDecision': 'PENDING',
          'requestedAt': '2026-10-15T04:00:00Z',
          'decidedAt': null,
        },
      });

      final extension = await service.requestExtension(orderId: 100, assignmentId: 7, extraHours: 1.5);

      expect(seen.single.method, 'POST');
      expect(seen.single.path, '/api/booking/orders/100/extensions');
      expect(jsonDecode(seen.single.body), {'assignmentId': 7, 'extraHours': 1.5});
      expect(extension.extensionId, 9);
      expect(extension.extraAmount, 97500);
      expect(extension.requestedAt, DateTime.utc(2026, 10, 15, 4));
    });

    test('a second extension carries EXTENSION_EXISTS', () async {
      await answer(409, {
        'success': false,
        'message': 'This order already has an extension.',
        'data': {'code': 'EXTENSION_EXISTS'},
      });

      await expectLater(
        service.requestExtension(orderId: 100, assignmentId: 7, extraHours: 1),
        throwsA(isA<ApiException>().having((e) => e.code, 'code', 'EXTENSION_EXISTS')),
      );
    });

    test('an answer without data is a failure, not an empty object', () async {
      await answer(200, {'success': true, 'message': '', 'data': null});

      await expectLater(service.getOrder(100), throwsA(isA<ApiException>()));
    });
  });
}
