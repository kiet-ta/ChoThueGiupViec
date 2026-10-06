import 'dart:convert';
import 'dart:io';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/auth/token_storage.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/network/api_response.dart';

void main() {
  group('ApiResponse Envelope Tests', () {
    test('deserializes successful response correctly', () {
      final json = {
        'success': true,
        'message': 'Thành công',
        'data': {'id': 123, 'name': 'Test'},
      };

      final response = ApiResponse<Map<String, dynamic>>.fromJson(json);

      expect(response.success, isTrue);
      expect(response.message, 'Thành công');
      expect(response.data?['id'], 123);
      expect(response.data?['name'], 'Test');
    });

    test('deserializes with custom fromJson transformer', () {
      final json = {
        'success': true,
        'message': 'OK',
        'data': [1, 2, 3],
      };

      final response = ApiResponse<List<int>>.fromJson(
        json,
        (data) => (data as List).map((e) => e as int).toList(),
      );

      expect(response.success, isTrue);
      expect(response.data, [1, 2, 3]);
    });

    test('serializes back to JSON map cleanly', () {
      const response = ApiResponse<String>(
        success: true,
        message: 'Saved',
        data: 'item-1',
      );

      final json = response.toJson();

      expect(json['success'], isTrue);
      expect(json['message'], 'Saved');
      expect(json['data'], 'item-1');
    });
  });

  group('TokenStorage Tests', () {
    test('manages access and refresh tokens lifecycle', () {
      final storage = TokenStorage();
      storage.clear();

      expect(storage.isAuthenticated, isFalse);
      expect(storage.hasRefreshToken, isFalse);

      storage.saveTokens(
        accessToken: 'access_123',
        refreshToken: 'refresh_456',
        expiresInSeconds: 3600,
      );

      expect(storage.isAuthenticated, isTrue);
      expect(storage.accessToken, 'access_123');
      expect(storage.refreshToken, 'refresh_456');
      expect(storage.isAccessTokenExpired, isFalse);

      storage.clear();
      expect(storage.isAuthenticated, isFalse);
      expect(storage.accessToken, isNull);
    });
  });

  group('ApiClient Tests', () {
    late HttpServer server;
    late ApiClient client;
    late TokenStorage storage;

    setUp(() async {
      storage = TokenStorage();
      storage.clear();

      server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
      client = ApiClient(
        baseUrl: 'http://${server.address.host}:${server.port}',
        tokenStorage: storage,
      );
    });

    tearDown(() async {
      await server.close(force: true);
    });

    test('performs GET request and attaches Bearer header when token present', () async {
      storage.saveTokens(accessToken: 'my_bearer_token', refreshToken: 'ref');

      server.listen((HttpRequest request) {
        expect(request.method, 'GET');
        expect(request.uri.path, '/api/test');
        expect(request.headers.value('authorization'), 'Bearer my_bearer_token');

        request.response.statusCode = 200;
        request.response.headers.contentType = ContentType.json;
        request.response.write(jsonEncode({
          'success': true,
          'message': 'OK',
          'data': {'greeting': 'hello'},
        }));
        request.response.close();
      });

      final res = await client.get<Map<String, dynamic>>('/api/test');

      expect(res.success, isTrue);
      expect(res.data?['greeting'], 'hello');
    });

    test('performs POST request with JSON payload', () async {
      server.listen((HttpRequest request) async {
        expect(request.method, 'POST');
        final content = await utf8.decodeStream(request);
        final map = jsonDecode(content);
        expect(map['title'], 'Clean');

        request.response.statusCode = 200;
        request.response.headers.contentType = ContentType.json;
        request.response.write(jsonEncode({
          'success': true,
          'message': 'Created',
          'data': {'id': 99},
        }));
        request.response.close();
      });

      final res = await client.post<Map<String, dynamic>>(
        '/api/items',
        body: {'title': 'Clean'},
      );

      expect(res.success, isTrue);
      expect(res.data?['id'], 99);
    });

    test('translates 400 validation error into ApiException with fieldErrors', () async {
      server.listen((HttpRequest request) {
        request.response.statusCode = 400;
        request.response.headers.contentType = ContentType.json;
        request.response.write(jsonEncode({
          'success': false,
          'message': 'Dữ liệu không hợp lệ',
          'data': {
            'errors': {
              'phoneNumber': ['Số điện thoại không đúng định dạng'],
            },
          },
        }));
        request.response.close();
      });

      try {
        await client.post('/api/validate', body: {});
        fail('Should throw ApiException');
      } on ApiException catch (e) {
        expect(e.statusCode, 400);
        expect(e.message, 'Dữ liệu không hợp lệ');
        expect(e.fieldErrors?['phoneNumber'], contains('Số điện thoại không đúng định dạng'));
      }
    });

    test('translates 429 rate limit into ApiException with retryAfterSeconds', () async {
      server.listen((HttpRequest request) {
        request.response.statusCode = 429;
        request.response.headers.set('retry-after', '45');
        request.response.headers.contentType = ContentType.json;
        request.response.write(jsonEncode({
          'success': false,
          'message': 'Quá nhiều yêu cầu. Vui lòng thử lại sau.',
        }));
        request.response.close();
      });

      try {
        await client.get('/api/limited');
        fail('Should throw ApiException');
      } on ApiException catch (e) {
        expect(e.statusCode, 429);
        expect(e.retryAfterSeconds, 45);
      }
    });
  });
}
