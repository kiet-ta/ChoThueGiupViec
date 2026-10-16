import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/auth/token_storage.dart' as core_storage;
import 'package:mobile/features/identity/models/auth_models.dart';
import 'package:mobile/features/identity/services/token_storage.dart';

void main() {
  sessionSharedWithApiClient();
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

// MOB-NAV: the login must reach the storage ApiClient reads, otherwise every feature call goes out without a Bearer token.
void sessionSharedWithApiClient() {
  group('One session for the identity storage and the API client', () {
    test('saveAuth puts the access token where ApiClient reads it', () {
      final identity = TokenStorage();
      final core = core_storage.TokenStorage();
      identity.clear();

      identity.saveAuth(
        const AuthResult(
          tokenType: 'Bearer',
          accessToken: 'shared_access',
          accessTokenExpiresInSeconds: 900,
          refreshToken: 'shared_refresh',
          user: AuthUser(id: 7, role: 'Customer', isNewUser: false),
        ),
      );

      expect(core.accessToken, 'shared_access');
      expect(core.refreshToken, 'shared_refresh');
      expect(core.isAuthenticated, isTrue);
    });

    test(
      'a refresh done by the API client is seen by the identity storage',
      () {
        final identity = TokenStorage();
        final core = core_storage.TokenStorage();
        identity.clear();

        core.saveTokens(accessToken: 'new_access', refreshToken: 'new_refresh');

        expect(identity.accessToken, 'new_access');
        expect(identity.isAuthenticated, isTrue);
      },
    );

    test('clear signs out of both', () {
      final identity = TokenStorage();
      final core = core_storage.TokenStorage();
      core.saveTokens(accessToken: 'a', refreshToken: 'r');

      identity.clear();

      expect(core.accessToken, isNull);
      expect(core.isAuthenticated, isFalse);
    });

    test('a registration token signs the core storage out', () {
      final identity = TokenStorage();
      final core = core_storage.TokenStorage();
      core.saveTokens(accessToken: 'a', refreshToken: 'r');

      identity.saveRegistration(
        const RegistrationRequired(
          isNewUser: true,
          registrationToken: 'reg',
          registrationTokenExpiresInSeconds: 1800,
        ),
      );

      expect(core.accessToken, isNull);
      expect(identity.registrationToken, 'reg');
    });
  });
}
