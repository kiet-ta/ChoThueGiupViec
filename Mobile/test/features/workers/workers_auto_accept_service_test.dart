import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/auth/token_storage.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/workers/services/workers_auto_accept_service.dart';

/// `GET` / `PUT /api/v1/workers/me/auto-accept` (contract workers.md 2.3C, DISPATCH-AUTO-01 #890).
void main() {
  late HttpServer server;
  late WorkersAutoAcceptService service;
  final methods = <String>[];
  final paths = <String>[];
  final bodies = <String>[];
  var status = 200;
  Map<String, dynamic> reply = {};

  setUp(() async {
    methods.clear();
    paths.clear();
    bodies.clear();
    status = 200;
    reply = {
      'success': true,
      'message': 'OK',
      'data': {'enabled': true},
    };
    final storage = TokenStorage()
      ..clear()
      ..saveTokens(accessToken: 'worker_token', refreshToken: 'ref');

    server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
    server.listen((HttpRequest request) async {
      methods.add(request.method);
      paths.add(request.uri.path);
      bodies.add(await utf8.decodeStream(request));
      request.response.statusCode = status;
      request.response.headers.contentType = ContentType.json;
      request.response.write(jsonEncode(reply));
      await request.response.close();
    });

    service = WorkersAutoAcceptService(
      apiClient: ApiClient(
        baseUrl: 'http://${server.address.host}:${server.port}',
        tokenStorage: storage,
      ),
    );
  });

  tearDown(() async => server.close(force: true));

  test('reads the stored value', () async {
    expect(await service.isEnabled(), isTrue);
    expect(methods, ['GET']);
    expect(paths, ['/api/v1/workers/me/auto-accept']);
  });

  test('sends { enabled } and returns what the server kept', () async {
    reply = {
      'success': true,
      'message': 'Auto-accept turned off.',
      'data': {'enabled': false},
    };

    expect(await service.setEnabled(false), isFalse);
    expect(methods, ['PUT']);
    expect(jsonDecode(bodies.single), {'enabled': false});
  });

  test('a refusal carries the server code and sentence', () async {
    status = 409;
    reply = {
      'success': false,
      'message':
          'Bạn cần đăng ký khu vực làm việc trước khi bật tự động nhận đơn.',
      'data': {'code': 'WORK_AREA_REQUIRED'},
    };

    final error = await service
        .setEnabled(true)
        .then<ApiException?>(
          (_) => null,
          onError: (Object e) => e as ApiException,
        );

    expect(error, isNotNull);
    expect(error!.statusCode, 409);
    expect(error.code, WorkersAutoAcceptService.workAreaRequired);
    expect(error.message, contains('khu vực làm việc'));
  });
}
