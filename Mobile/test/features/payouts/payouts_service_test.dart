import 'dart:convert';
import 'dart:io';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/auth/token_storage.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/payouts/services/payouts_service.dart';

void main() {
  group('PayoutsService over a local server', () {
    late HttpServer server;
    late PayoutsService service;
    final seen = <({String method, String path, Map<String, String> query})>[];

    Future<void> answer(int status, Object? json) async {
      server.listen((HttpRequest request) async {
        await utf8.decodeStream(request);
        seen.add((method: request.method, path: request.uri.path, query: request.uri.queryParameters));
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
      service = PayoutsService(client: ApiClient(baseUrl: 'http://${server.address.host}:${server.port}'));
    });

    tearDown(() async {
      await server.close(force: true);
      TokenStorage().clear();
    });

    test('reads the earnings of a month from the workers path with the month in the query', () async {
      await answer(200, {
        'success': true,
        'message': '',
        'data': {
          'periodMonth': '2026-09',
          'jobCount': 1,
          'grossAmount': 400000,
          'commissionAmount': 80000,
          'penaltyAmount': 0,
          'netAmount': 320000,
          'payoutStatus': 'TRANSFERRED',
          'jobs': [],
        },
      });

      final e = await service.getEarnings('2026-09');

      expect(seen.single.method, 'GET');
      expect(seen.single.path, '/api/workers/me/earnings');
      expect(seen.single.query, {'month': '2026-09'});
      expect(e.netAmount, 320000);
      expect(e.payoutStatus, 'TRANSFERRED');
    });

    test('reads one page of the payout history with the paging query', () async {
      await answer(200, {
        'success': true,
        'message': '',
        'data': {
          'items': [
            {'batchId': 5, 'periodMonth': '2026-08', 'netAmount': 900000, 'itemStatus': 'TRANSFERRED', 'transferredAt': '2026-09-03T02:00:00Z'},
          ],
          'page': 2,
          'pageSize': 10,
          'total': 11,
        },
      });

      final page = await service.getPayouts(page: 2, pageSize: 10);

      expect(seen.single.path, '/api/workers/me/payouts');
      expect(seen.single.query, {'page': '2', 'pageSize': '10'});
      expect(page.items.single.netAmount, 900000);
      expect(page.total, 11);
    });

    test('a 403 for an agency staff member is thrown as a forbidden ApiException', () async {
      await answer(403, {'success': false, 'message': 'Forbidden', 'data': null});

      await expectLater(
        service.getEarnings('2026-09'),
        throwsA(isA<ApiException>().having((e) => e.isForbidden, 'isForbidden', isTrue)),
      );
    });

    test('a 400 for a future month keeps its status', () async {
      await answer(400, {'success': false, 'message': 'month cannot be in the future', 'data': null});

      await expectLater(
        service.getEarnings('2099-01'),
        throwsA(isA<ApiException>().having((e) => e.statusCode, 'status', 400)),
      );
    });

    test('an answer without data is an error, not a silent empty month', () async {
      await answer(200, {'success': true, 'message': '', 'data': null});

      await expectLater(service.getEarnings('2026-09'), throwsA(isA<ApiException>()));
      expect(seen, hasLength(1));
    });
  });
}
