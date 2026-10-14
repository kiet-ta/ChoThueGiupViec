import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/auth/token_storage.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/booking/models/fallback_models.dart';
import 'package:mobile/features/booking/services/fallback_service.dart';

void main() {
  group('FallbackService over a local server', () {
    late HttpServer server;
    late FallbackService service;
    final seen = <({String method, String path, String body})>[];

    setUp(() async {
      seen.clear();
      TokenStorage().clear();
      TokenStorage().saveTokens(accessToken: 'tok', refreshToken: 'ref');
      server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
      service = FallbackService(
        client: ApiClient(
          baseUrl: 'http://${server.address.host}:${server.port}',
        ),
      );
      server.listen((HttpRequest request) async {
        final body = await utf8.decodeStream(request);
        seen.add((method: request.method, path: request.uri.path, body: body));
        request.response.headers.contentType = ContentType.json;
        request.response.write(
          jsonEncode({
            'success': true,
            'message': '',
            'data': request.method == 'GET'
                ? {
                    'orderId': 100,
                    'state': 'AWAITING_CUSTOMER',
                    'workerId': 101,
                    'secondShiftCode': 'SHIFT_AFTERNOON',
                    'secondShiftDate': '2026-10-15',
                    'deadlineUtc': '2026-10-15T01:00:00Z',
                  }
                : {
                    'orderId': 100,
                    'outcome': 'WORKER_ASKED',
                    'workerDeadlineUtc': '2026-10-14T03:02:00Z',
                    'message': null,
                  },
          }),
        );
        await request.response.close();
      });
    });

    tearDown(() async {
      await server.close(force: true);
      TokenStorage().clear();
    });

    test('getStatus reads the fallback of the order', () async {
      final status = await service.getStatus(100);

      expect(seen.single.method, 'GET');
      expect(seen.single.path, '/api/dispatch/orders/100/fallback');
      expect(status.state, FallbackState.awaitingCustomer);
      expect(status.workerId, 101);
      expect(status.secondShiftCode, 'SHIFT_AFTERNOON');
      expect(status.deadline, DateTime.utc(2026, 10, 15, 1));
    });

    test('respond posts the customer choice', () async {
      final response = await service.respond(100, accept: true);

      expect(seen.single.method, 'POST');
      expect(seen.single.path, '/api/dispatch/orders/100/fallback/respond');
      expect(jsonDecode(seen.single.body), {'accept': true});
      expect(response.outcome, FallbackOutcome.workerAsked);
      expect(response.workerDeadline, DateTime.utc(2026, 10, 14, 3, 2));
    });
  });
}
