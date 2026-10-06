import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/models/user_role.dart';
import 'package:mobile/features/identity/services/api_error.dart';
import 'package:mobile/features/identity/services/auth_service.dart';
import 'package:mobile/features/identity/services/token_storage.dart';

import 'fake_backend.dart';

/// AuthService against a real HTTP server, with the default (non-mock) mode of the real app.
void main() {
  late FakeBackend backend;
  late TokenStorage storage;

  setUp(() async {
    backend = FakeBackend();
    await backend.start();
    storage = TokenStorage()..clear();
  });

  tearDown(() => backend.stop());

  AuthService service([String? baseUrl]) => AuthService(baseUrl: baseUrl ?? backend.baseUrl, tokenStorage: storage);

  test('the real app does not use mock mode by default', () {
    expect(AuthService().useMockFallback, isFalse);
  });

  group('requestOtp', () {
    test('200 returns the server TTL and cooldown', () async {
      backend.on('POST', '/api/auth/otp/request', 200,
          FakeBackend.ok({'expiresInSeconds': 120, 'resendAvailableInSeconds': 30}));

      final result = await service().requestOtp(phoneNumber: '+84912345678', role: AppRole.worker);

      expect(result.expiresInSeconds, 120);
      expect(result.resendAvailableInSeconds, 30);
      expect(backend.requestBodies['POST /api/auth/otp/request'], {'phoneNumber': '0912345678', 'role': 'Worker'});
    });

    test('429 throws with the server message and Retry-After, instead of pretending the OTP was sent', () async {
      backend.on('POST', '/api/auth/otp/request', 429, FakeBackend.error('Vui lòng chờ 45 giây.'),
          {'Retry-After': '45'});

      await expectLater(
        service().requestOtp(phoneNumber: '0912345678', role: AppRole.customer),
        throwsA(isA<ApiException>()
            .having((e) => e.statusCode, 'statusCode', 429)
            .having((e) => e.message, 'message', 'Vui lòng chờ 45 giây.')
            .having((e) => e.retryAfterSeconds, 'retryAfterSeconds', 45)),
      );
    });

    test('backend unreachable throws a network error (status 0)', () async {
      await expectLater(
        service(FakeBackend.unreachableBaseUrl).requestOtp(phoneNumber: '0912345678', role: AppRole.customer),
        throwsA(isA<ApiException>().having((e) => e.isNetworkError, 'isNetworkError', isTrue)),
      );
    });
  });

  group('verifyOtp', () {
    test('200 saves the real tokens', () async {
      backend.on('POST', '/api/auth/otp/verify', 200, FakeBackend.ok({
        'tokenType': 'Bearer',
        'accessToken': 'real-access',
        'accessTokenExpiresInSeconds': 900,
        'refreshToken': 'real-refresh',
        'user': {'id': 7, 'role': 'Customer', 'isNewUser': false},
      }));

      final response = await service().verifyOtp(phoneNumber: '0912345678', role: AppRole.customer, code: '123456');

      expect(response.isSuccess, isTrue);
      expect(storage.accessToken, 'real-access');
      expect(storage.currentUser?.id, 7);
    });

    test('401 returns the server message and saves no token', () async {
      backend.on('POST', '/api/auth/otp/verify', 401, FakeBackend.error('Mã không đúng hoặc đã hết hạn.'));

      final response = await service().verifyOtp(phoneNumber: '0912345678', role: AppRole.customer, code: '123456');

      expect(response.isSuccess, isFalse);
      expect(response.statusCode, 401);
      expect(response.errorMessage, 'Mã không đúng hoặc đã hết hạn.');
      expect(storage.isAuthenticated, isFalse);
    });

    test('502 with a non-JSON body is an error, not a mock login', () async {
      backend.on('POST', '/api/auth/otp/verify', 502, '<html>Bad Gateway</html>');

      final response = await service().verifyOtp(phoneNumber: '0912345678', role: AppRole.customer, code: '123456');

      expect(response.isSuccess, isFalse);
      expect(response.statusCode, 502);
      expect(response.errorMessage, contains('502'));
      expect(storage.accessToken, isNull);
      expect(storage.isAuthenticated, isFalse);
    });

    test('backend unreachable is a network error, not a mock login', () async {
      final response = await service(FakeBackend.unreachableBaseUrl)
          .verifyOtp(phoneNumber: '0912345678', role: AppRole.customer, code: '424242');

      expect(response.isSuccess, isFalse);
      expect(response.statusCode, 0);
      expect(response.errorMessage, contains('Không kết nối được máy chủ'));
      expect(storage.isAuthenticated, isFalse);
    });
  });
}
