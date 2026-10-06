import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/identity/models/auth_models.dart';
import 'package:mobile/features/identity/services/token_storage.dart';

void main() {
  group('TokenStorage Tests', () {
    late TokenStorage storage;

    setUp(() {
      storage = TokenStorage();
      storage.clear();
    });

    test('Initial state is unauthenticated', () {
      expect(storage.isAuthenticated, isFalse);
      expect(storage.accessToken, isNull);
      expect(storage.refreshToken, isNull);
      expect(storage.currentUser, isNull);
    });

    test('saveAuth saves tokens and user metadata correctly', () {
      const auth = AuthResult(
        tokenType: 'Bearer',
        accessToken: 'sample_access_token',
        accessTokenExpiresInSeconds: 900,
        refreshToken: 'sample_refresh_token',
        user: AuthUser(id: 42, role: 'Customer', isNewUser: false),
      );

      storage.saveAuth(auth);

      expect(storage.isAuthenticated, isTrue);
      expect(storage.accessToken, 'sample_access_token');
      expect(storage.refreshToken, 'sample_refresh_token');
      expect(storage.currentUser?.id, 42);
      expect(storage.currentUser?.role, 'Customer');
      expect(storage.currentUser?.isNewUser, isFalse);
    });

    test('saveRegistration saves registration token for new worker', () {
      const reg = RegistrationRequired(
        isNewUser: true,
        registrationToken: 'worker_reg_token_xyz',
        registrationTokenExpiresInSeconds: 1800,
      );

      storage.saveRegistration(reg);

      expect(storage.hasRegistrationToken, isTrue);
      expect(storage.registrationToken, 'worker_reg_token_xyz');
      expect(storage.accessToken, isNull);
      expect(storage.currentUser?.role, 'Worker');
    });

    test('clear wipes all stored tokens', () {
      const auth = AuthResult(
        tokenType: 'Bearer',
        accessToken: 'token_to_clear',
        accessTokenExpiresInSeconds: 900,
        refreshToken: 'refresh_to_clear',
        user: AuthUser(id: 1, role: 'Customer', isNewUser: false),
      );

      storage.saveAuth(auth);
      expect(storage.isAuthenticated, isTrue);

      storage.clear();
      expect(storage.isAuthenticated, isFalse);
      expect(storage.accessToken, isNull);
      expect(storage.refreshToken, isNull);
      expect(storage.currentUser, isNull);
    });
  });
}
