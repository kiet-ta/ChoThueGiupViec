import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/models/user_role.dart';
import 'package:mobile/features/identity/services/auth_service.dart';
import 'package:mobile/features/identity/services/token_storage.dart';

void main() {
  group('AuthService Tests', () {
    late AuthService authService;
    late TokenStorage storage;

    setUp(() {
      storage = TokenStorage();
      storage.clear();
      authService = AuthService(tokenStorage: storage, useMockFallback: true);
    });

    test('normalizePhone validates Vietnamese mobile formats correctly', () {
      expect(AuthService.normalizePhone('0912345678'), '0912345678');
      expect(AuthService.normalizePhone('091 234 5678'), '0912345678');
      expect(AuthService.normalizePhone('+84912345678'), '0912345678');
      expect(AuthService.normalizePhone('84912345678'), '0912345678');
      expect(AuthService.normalizePhone('0381234567'), '0381234567');
      expect(AuthService.normalizePhone('0581234567'), '0581234567');
      expect(AuthService.normalizePhone('0781234567'), '0781234567');
      expect(AuthService.normalizePhone('0881234567'), '0881234567');

      // Invalid formats
      expect(AuthService.normalizePhone('123456'), isNull);
      expect(AuthService.normalizePhone('0123456789'), isNull); // 01 is not mobile
      expect(AuthService.normalizePhone('0283838383'), isNull); // landline
      expect(AuthService.normalizePhone('abcd'), isNull);
    });

    test('requestOtp succeeds and returns expected TTL and cooldown', () async {
      final result = await authService.requestOtp(
        phoneNumber: '0912345678',
        role: AppRole.customer,
      );

      expect(result.expiresInSeconds, 300);
      expect(result.resendAvailableInSeconds, 60);
    });

    test('requestOtp throws FormatException for invalid phone', () async {
      expect(
        () => authService.requestOtp(phoneNumber: 'invalid', role: AppRole.customer),
        throwsFormatException,
      );
    });

    test('verifyOtp succeeds and populates TokenStorage for valid code', () async {
      final response = await authService.verifyOtp(
        phoneNumber: '0912345678',
        role: AppRole.customer,
        code: '123456',
      );

      expect(response.isSuccess, isTrue);
      expect(response.authResult, isNotNull);
      expect(storage.isAuthenticated, isTrue);
      expect(storage.currentUser?.role, 'Customer');
    });

    test('verifyOtp returns 401 error message for code 000000', () async {
      final response = await authService.verifyOtp(
        phoneNumber: '0912345678',
        role: AppRole.customer,
        code: '000000',
      );

      expect(response.isSuccess, isFalse);
      expect(response.statusCode, 401);
      expect(response.errorMessage, isNotNull);
      expect(storage.isAuthenticated, isFalse);
    });

    test('verifyOtp rejects invalid code length', () async {
      final response = await authService.verifyOtp(
        phoneNumber: '0912345678',
        role: AppRole.customer,
        code: '123',
      );

      expect(response.isSuccess, isFalse);
      expect(response.statusCode, 400);
      expect(response.errorMessage, contains('6 chữ số'));
    });
  });
}
