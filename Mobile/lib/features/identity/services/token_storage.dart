import '../../../core/auth/token_storage.dart' as core;
import '../models/auth_models.dart';

/// The signed-in session of the app: the tokens, the user and the one-off registration token of a new worker.
///
/// The tokens live in the core storage (`core/auth/token_storage.dart`), the one `ApiClient` reads for every authenticated call of every
/// feature (Booking, Workers, Dispatch, Payments, ...). This class used to keep its own copy, so a login filled a storage that `ApiClient` never
/// looked at and those calls went out without a Bearer token (MOB-NAV). It now hands everything to the core storage: one session.
class TokenStorage {
  static final TokenStorage _instance = TokenStorage._internal();
  factory TokenStorage() => _instance;
  TokenStorage._internal();

  final core.TokenStorage _core = core.TokenStorage();

  String? _registrationToken;
  AuthUser? _currentUser;

  bool get isAuthenticated => _core.isAuthenticated;
  bool get hasRefreshToken => _core.refreshToken != null;
  bool get hasRegistrationToken => _registrationToken != null;

  String? get accessToken => _core.accessToken;
  String? get refreshToken => _core.refreshToken;
  String? get registrationToken => _registrationToken;
  AuthUser? get currentUser => _currentUser;

  bool get isAccessTokenExpired => _core.isAccessTokenExpired;

  void saveAuth(AuthResult result) {
    _core.saveTokens(
      accessToken: result.accessToken,
      refreshToken: result.refreshToken,
      expiresInSeconds: result.accessTokenExpiresInSeconds,
    );
    _currentUser = result.user;
    _registrationToken = null;
  }

  void saveRegistration(RegistrationRequired reg) {
    _registrationToken = reg.registrationToken;
    _core.clear();
    _currentUser = const AuthUser(id: 0, role: 'Worker', isNewUser: true);
  }

  void clear() {
    _core.clear();
    _registrationToken = null;
    _currentUser = null;
  }
}
