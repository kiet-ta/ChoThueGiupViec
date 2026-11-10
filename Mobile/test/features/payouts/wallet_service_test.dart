import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/auth/token_storage.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/payouts/services/wallet_service.dart';

void main() {
  group('WalletService over a local server', () {
    late HttpServer server;
    late WalletService service;
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
      service = WalletService(
        client: ApiClient(
          baseUrl: 'http://${server.address.host}:${server.port}',
        ),
      );
    });

    tearDown(() async {
      await server.close(force: true);
      TokenStorage().clear();
    });

    test('reads the wallet from the workers path', () async {
      await answer(200, {
        'success': true,
        'message': '',
        'data': {
          'balance': 288000,
          'available': 188000,
          'held': 100000,
          'pendingWithdrawal': {
            'withdrawalId': 7,
            'amount': 100000,
            'status': 'PENDING',
            'requestedAt': '2026-11-05T02:00:00Z',
          },
          'nextAvailableAt': null,
        },
      });

      final wallet = await service.getWallet();

      expect(seen.single.method, 'GET');
      expect(seen.single.path, '/api/v1/workers/me/wallet');
      expect(wallet.available, 188000);
      expect(wallet.pendingWithdrawal!.amount, 100000);
    });

    test(
      'sends the amount as a whole number in the body of the request',
      () async {
        await answer(201, {
          'success': true,
          'message': 'Withdrawal requested.',
          'data': {
            'withdrawalId': 8,
            'amount': 100000,
            'status': 'PENDING',
            'requestedAt': '2026-11-05T02:00:00Z',
            'reviewedAt': null,
            'rejectReason': null,
          },
        });

        final created = await service.requestWithdrawal(100000);

        expect(seen.single.method, 'POST');
        expect(seen.single.path, '/api/v1/workers/me/withdrawals');
        expect(jsonDecode(seen.single.body), {'amount': 100000});
        expect(created.withdrawalId, 8);
        expect(created.status, 'PENDING');
      },
    );

    test('cancels a request on its own path', () async {
      await answer(200, {
        'success': true,
        'message': '',
        'data': {
          'withdrawalId': 8,
          'amount': 100000,
          'status': 'CANCELLED',
          'requestedAt': '2026-11-05T02:00:00Z',
          'reviewedAt': null,
          'rejectReason': null,
        },
      });

      final cancelled = await service.cancelWithdrawal(8);

      expect(seen.single.method, 'POST');
      expect(seen.single.path, '/api/v1/workers/me/withdrawals/8/cancel');
      expect(cancelled.status, 'CANCELLED');
    });

    test('throws the business code of a conflict', () async {
      await answer(409, {
        'success': false,
        'message': 'Pending request exists.',
        'data': {'code': 'WITHDRAWAL_PENDING'},
      });

      await expectLater(
        service.requestWithdrawal(100000),
        throwsA(
          isA<ApiException>()
              .having((e) => e.statusCode, 'status', 409)
              .having((e) => e.code, 'code', 'WITHDRAWAL_PENDING'),
        ),
      );
    });

    test('throws a 403 for an agency worker', () async {
      await answer(403, {
        'success': false,
        'message': 'Forbidden',
        'data': null,
      });

      await expectLater(
        service.getWallet(),
        throwsA(
          isA<ApiException>().having((e) => e.isForbidden, '403', isTrue),
        ),
      );
    });
  });
}
