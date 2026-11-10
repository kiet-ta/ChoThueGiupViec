import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/auth/token_storage.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/payouts/services/ledger_service.dart';

void main() {
  group('LedgerService over a local server', () {
    late HttpServer server;
    late LedgerService service;
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
      service = LedgerService(
        client: ApiClient(
          baseUrl: 'http://${server.address.host}:${server.port}',
        ),
      );
    });

    tearDown(() async {
      await server.close(force: true);
      TokenStorage().clear();
    });

    test('reads the history with the month days, the type and the paging in the query', () async {
      await answer(200, {
        'success': true,
        'message': '',
        'data': {
          'items': [],
          'openingBalance': 100000,
          'closingBalance': 308000,
          'page': 2,
          'pageSize': 20,
          'total': 25,
        },
      });

      final page = await service.getLedger(
        from: '2026-11-01',
        to: '2026-11-30',
        type: 'WITHDRAWAL',
        page: 2,
      );

      expect(seen.single.method, 'GET');
      expect(seen.single.path, '/api/v1/workers/me/ledger');
      expect(seen.single.query, {
        'from': '2026-11-01',
        'to': '2026-11-30',
        'type': 'WITHDRAWAL',
        'page': '2',
        'pageSize': '20',
      });
      expect(
        [page.openingBalance, page.closingBalance, page.total],
        [100000, 308000, 25],
      );
    });

    test('leaves out the filters that are not set', () async {
      await answer(200, {
        'success': true,
        'message': '',
        'data': {
          'items': [],
          'openingBalance': 0,
          'closingBalance': 0,
          'page': 1,
          'pageSize': 20,
          'total': 0,
        },
      });

      await service.getLedger();

      expect(seen.single.query.keys, unorderedEquals(['page', 'pageSize']));
    });

    test('reads the statement of a month from its own path', () async {
      await answer(200, {
        'success': true,
        'message': '',
        'data': {
          'periodMonth': '2026-10',
          'openingBalance': 0,
          'items': [],
          'advanceAmount': 100000,
          'penaltyAmount': 0,
          'settlementAmount': 108000,
          'closingBalance': 0,
          'lastHash': 'ff00',
        },
      });

      final s = await service.getStatement('2026-10');

      expect(seen.single.path, '/api/v1/workers/me/statements/2026-10');
      expect(
        [s.advanceAmount, s.settlementAmount, s.lastHash],
        [100000, 108000, 'ff00'],
      );
    });

    test('throws a 403 for an agency worker', () async {
      await answer(403, {
        'success': false,
        'message': 'Forbidden',
        'data': null,
      });
      await expectLater(
        service.getLedger(),
        throwsA(
          isA<ApiException>().having((e) => e.isForbidden, '403', isTrue),
        ),
      );
    });
  });
}
