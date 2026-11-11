import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/auth/token_storage.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/payouts/services/worker_bank_service.dart';

void main() {
  group('WorkerBankService over a local server', () {
    late HttpServer server;
    late WorkerBankService service;
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
      service = WorkerBankService(
        client: ApiClient(
          baseUrl: 'http://${server.address.host}:${server.port}',
        ),
      );
    });

    tearDown(() async {
      await server.close(force: true);
      TokenStorage().clear();
    });

    test('reads the account from the workers path', () async {
      await answer(200, {
        'success': true,
        'message': '',
        'data': {
          'bankName': 'ACB',
          'bankAccountNo': '0011001234567',
          'isSet': true,
        },
      });

      final bank = await service.getBank();

      expect(seen.single.method, 'GET');
      expect(seen.single.path, '/api/v1/workers/me/bank');
      expect([bank.bankName, bank.isSet], ['ACB', true]);
    });

    test(
      'saves the account with a PUT and the two fields in the body',
      () async {
        await answer(200, {
          'success': true,
          'message': '',
          'data': {
            'bankName': 'Vietcombank',
            'bankAccountNo': '0011001234567',
            'isSet': true,
          },
        });

        final bank = await service.saveBank(
          bankName: 'Vietcombank',
          bankAccountNo: '0011001234567',
        );

        expect(seen.single.method, 'PUT');
        expect(seen.single.path, '/api/v1/workers/me/bank');
        expect(jsonDecode(seen.single.body), {
          'bankName': 'Vietcombank',
          'bankAccountNo': '0011001234567',
        });
        expect(bank.isSet, isTrue);
      },
    );

    test('throws the field messages of a 400', () async {
      await answer(400, {
        'success': false,
        'message': 'Validation failed',
        'data': {
          'errors': {
            'bankAccountNo': ['Bank account number must be 6 to 30 digits.'],
          },
        },
      });

      await expectLater(
        service.saveBank(bankName: 'ACB', bankAccountNo: '12'),
        throwsA(
          isA<ApiException>()
              .having((e) => e.statusCode, 'status', 400)
              .having(
                (e) => e.fieldErrors?['bankAccountNo']?.first,
                'field',
                contains('6 to 30'),
              ),
        ),
      );
    });
  });
}
