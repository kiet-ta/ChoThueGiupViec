import '../models/auth_models.dart';

/// Secure in-memory and persistent token storage abstraction for Mobile.
class TokenStorage {
  static final TokenStorage _instance = TokenStorage._internal();
  factory TokenStorage() => _instance;
  TokenStorage._internal();

  String? _accessToken;
  String? _refreshToken;
  String? _registrationToken;
  AuthUser? _currentUser;
  DateTime? _accessExpiresAt;

  bool get isAuthenticated => _accessToken != null && !isAccessTokenExpired;
  bool get hasRefreshToken => _refreshToken != null;
  bool get hasRegistrationToken => _registrationToken != null;

  String? get accessToken => _accessToken;
  String? get refreshToken => _refreshToken;
  String? get registrationToken => _registrationToken;
  AuthUser? get currentUser => _currentUser;

  bool get isAccessTokenExpired {
    if (_accessExpiresAt == null) return false;
    return DateTime.now().isAfter(_accessExpiresAt!);
  }

  void saveAuth(AuthResult result) {
    _accessToken = result.accessToken;
    _refreshToken = result.refreshToken;
    _currentUser = result.user;
    _accessExpiresAt = DateTime.now().add(
      Duration(seconds: result.accessTokenExpiresInSeconds),
    );
    _registrationToken = null;
  }

  void saveRegistration(RegistrationRequired reg) {
    _registrationToken = reg.registrationToken;
    _accessToken = null;
    _refreshToken = null;
    _currentUser = const AuthUser(id: 0, role: 'Worker', isNewUser: true);
  }

  void clear() {
    _accessToken = null;
    _refreshToken = null;
    _registrationToken = null;
    _currentUser = null;
    _accessExpiresAt = null;
  }
}
