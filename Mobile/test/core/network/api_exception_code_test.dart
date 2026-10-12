import 'dart:convert';
import 'dart:io';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/auth/token_storage.dart';
import 'package:mobile/core/network/api_client.dart';

/// MOB-M2-01/02/03: `ApiException.code` carries the business code of a failed call (`data.code`), so screens branch on
/// the code and never on the message (contracts booking.md 3.3, payments.md 2.1).
void main() {
  group('ApiException.code over a local server', () {
    late HttpServer server;
    late ApiClient client;

    Future<void> answer(int status, Object? json) async {
      server.listen((HttpRequest request) async {
        await utf8.decodeStream(request);
        request.response.statusCode = status;
        request.response.headers.contentType = ContentType.json;
        request.response.write(jsonEncode(json));
        await request.response.close();
      });
    }

    Future<ApiException> failure() async {
      try {
        await client.post<Object?>('/api/booking/orders', body: const {});
      } on ApiException catch (e) {
        return e;
      }
      fail('the call should have thrown');
    }

    setUp(() async {
      TokenStorage().clear();
      TokenStorage().saveTokens(accessToken: 'tok', refreshToken: 'ref');
      server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
      client = ApiClient(baseUrl: 'http://${server.address.host}:${server.port}');
    });

    tearDown(() async {
      await server.close(force: true);
      TokenStorage().clear();
    });

    test('reads data.code of a business 409', () async {
      await answer(409, {
        'success': false,
        'message': 'No partner agency has capacity for this shift.',
        'data': {'code': 'FULLY_BOOKED'},
      });

      final e = await failure();

      expect(e.statusCode, 409);
      expect(e.code, 'FULLY_BOOKED');
      expect(e.message, 'No partner agency has capacity for this shift.');
      expect(e.fieldErrors, isNull);
    });

    test('is null when the server sends no code', () async {
      await answer(409, {'success': false, 'message': 'Conflict', 'data': null});

      expect((await failure()).code, isNull);
    });

    test('is null for a 400 with field errors, which are still read', () async {
      await answer(400, {
        'success': false,
        'message': 'Validation failed',
        'data': {
          'errors': {
            'scheduledDate': ['too far ahead'],
          },
        },
      });

      final e = await failure();

      expect(e.code, isNull);
      expect(e.fieldErrors, {
        'scheduledDate': ['too far ahead'],
      });
    });

    test('ignores a code that is not a non-empty string', () async {
      await answer(409, {
        'success': false,
        'message': 'Conflict',
        'data': {'code': 42},
      });

      expect((await failure()).code, isNull);
    });
  });
}
