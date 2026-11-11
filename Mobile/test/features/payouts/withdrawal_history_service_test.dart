import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/auth/token_storage.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/payouts/services/withdrawal_history_service.dart';

void main() {
  group('WithdrawalHistoryService over a local server', () {
    late HttpServer server;
    late WithdrawalHistoryService service;
    final seen = <({String method, String path, Map<String, String> query})>[];

    Future<void> answer(int status, Object? json) async {
      server.listen((HttpRequest request) async {
        await utf8.decodeStream(request);
        seen.add((
          method: request.method,
          path: request.uri.path,
          query: request.uri.queryParameters,
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
      service = WithdrawalHistoryService(
        client: ApiClient(
          baseUrl: 'http://${server.address.host}:${server.port}',
        ),
      );
    });

    tearDown(() async {
      await server.close(force: true);
      TokenStorage().clear();
    });

    test('reads the worker requests from the workers path with the paging in the query', () async {
      await answer(200, {
        'success': true,
        'message': '',
        'data': {
          'items': [
            {
              'withdrawalId': 9,
              'amount': 50000,
              'status': 'PAID',
              'requestedAt': '2026-11-05T02:00:00Z',
              'reviewedAt': '2026-11-05T03:00:00Z',
              'rejectReason': null,
            },
          ],
          'page': 2,
          'pageSize': 10,
          'total': 11,
        },
      });

      final page = await service.getWithdrawals(page: 2, pageSize: 10);

      expect(seen.single.method, 'GET');
      expect(seen.single.path, '/api/v1/workers/me/withdrawals');
      expect(seen.single.query, {'page': '2', 'pageSize': '10'});
      expect(page.total, 11);
      expect(page.items.single.status, 'PAID');
    });

    test('throws a 403 for an agency worker', () async {
      await answer(403, {
        'success': false,
        'message': 'Forbidden',
        'data': null,
      });

      await expectLater(
        service.getWithdrawals(),
        throwsA(
          isA<ApiException>().having((e) => e.isForbidden, '403', isTrue),
        ),
      );
    });
  });
}
