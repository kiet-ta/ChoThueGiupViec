import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/auth/token_storage.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/workers/services/workers_profile_service.dart';

void main() {
  late HttpServer server;
  late ApiClient client;
  final seen = <({String method, String path, String body})>[];

  setUp(() async {
    seen.clear();
    final storage = TokenStorage()..clear();
    server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
    server.listen((request) async {
      seen.add((
        method: request.method,
        path: request.uri.path,
        body: await utf8.decodeStream(request),
      ));
      request.response
        ..headers.contentType = ContentType.json
        ..write(
          jsonEncode({
            'success': true,
            'message': 'OK',
            'data': {'fullName': 'Nguyễn Văn Thợi'},
          }),
        );
      await request.response.close();
    });
    client = ApiClient(
      baseUrl: 'http://${server.address.host}:${server.port}',
      tokenStorage: storage,
    );
  });

  tearDown(() => server.close(force: true));

  test('patch sends a PATCH with the JSON body', () async {
    await client.patch<void>('/api/v1/x', body: {'a': 1});

    expect(seen.single.method, 'PATCH');
    expect(seen.single.path, '/api/v1/x');
    expect(jsonDecode(seen.single.body), {'a': 1});
  });

  test('updateFullName PATCHes /workers/me with the trimmed name', () async {
    final name = await WorkersProfileService(apiClient: client)
        .updateFullName('  Nguyễn Văn Thợi  ');

    expect(name, 'Nguyễn Văn Thợi');
    expect(seen.single.method, 'PATCH');
    expect(seen.single.path, '/api/v1/workers/me');
    expect(jsonDecode(seen.single.body), {'fullName': 'Nguyễn Văn Thợi'});
  });

  test(
    'updateFullName rejects an empty or too long name, no request',
    () async {
      final service = WorkersProfileService(apiClient: client);

      await expectLater(service.updateFullName('   '), throwsArgumentError);
      await expectLater(service.updateFullName('a' * 101), throwsArgumentError);
      expect(seen, isEmpty);
    },
  );
}
