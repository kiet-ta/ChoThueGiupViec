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

    test('sends a change request with a POST to the bank-change-requests path and reads the pending state', () async {
      await answer(200, {
        'success': true,
        'message': '',
        'data': {
          'bankName': 'ACB',
          'bankAccountNo': '0011001234567',
          'isSet': true,
          'canRequestChange': false,
          'changeLockedUntil': '2026-11-27T18:10:30.4335228Z',
          'pendingRequest': {
            'requestId': 4,
            'bankName': 'Techcombank',
            'bankAccountNo': '8800770066',
            'requestedAt': '2026-11-12T18:10:30.4335228Z',
          },
          'lastRequest': {
            'status': 'PENDING',
            'rejectReason': null,
            'requestedAt': '2026-11-12T18:10:30.4335228Z',
            'reviewedAt': null,
          },
        },
      });

      final bank = await service.requestChange(
        bankName: 'Techcombank',
        bankAccountNo: '8800770066',
      );

      expect(seen.single.method, 'POST');
      expect(seen.single.path, '/api/v1/workers/me/bank-change-requests');
      expect(jsonDecode(seen.single.body), {
        'bankName': 'Techcombank',
        'bankAccountNo': '8800770066',
      });
      expect(bank.canRequestChange, isFalse);
      expect(bank.pendingRequest?.bankAccountNo, '8800770066');
      expect(
        bank.changeLockedUntil,
        DateTime.utc(2026, 11, 27, 18, 10, 30, 433, 522),
      );
      expect(bank.lastRequest?.status, 'PENDING');
    });

    test('a 409 of the lock is thrown with its code and message', () async {
      await answer(409, {
        'success': false,
        'message': 'Bạn đã gửi đơn đổi tài khoản gần đây. Có thể gửi đơn mới từ ngày 28/11/2026.',
        'data': {'code': 'BANK_CHANGE_LOCKED'},
      });

      await expectLater(
        service.requestChange(bankName: 'ACB', bankAccountNo: '123456'),
        throwsA(
          isA<ApiException>()
              .having((e) => e.statusCode, 'status', 409)
              .having((e) => e.code, 'code', 'BANK_CHANGE_LOCKED')
              .having((e) => e.message, 'message', contains('28/11/2026')),
        ),
      );
    });

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
