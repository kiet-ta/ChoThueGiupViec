/// Abstract contract for secure token persistence across app launches.
abstract class ITokenStorage {
  String? get accessToken;
  String? get refreshToken;
  bool get isAuthenticated;
  bool get isAccessTokenExpired;

  void saveTokens({
    required String accessToken,
    required String refreshToken,
    int? expiresInSeconds,
  });

  void clear();
}

/// In-memory and persistent token storage implementation for Mobile.
class TokenStorage implements ITokenStorage {
  static final TokenStorage _instance = TokenStorage._internal();
  factory TokenStorage() => _instance;
  TokenStorage._internal();

  String? _accessToken;
  String? _refreshToken;
  DateTime? _accessExpiresAt;

  @override
  bool get isAuthenticated => _accessToken != null && !isAccessTokenExpired;

  bool get hasRefreshToken => _refreshToken != null;

  @override
  String? get accessToken => _accessToken;

  @override
  String? get refreshToken => _refreshToken;

  @override
  bool get isAccessTokenExpired {
    if (_accessExpiresAt == null) return false;
    return DateTime.now().isAfter(_accessExpiresAt!);
  }

  @override
  void saveTokens({
    required String accessToken,
    required String refreshToken,
    int? expiresInSeconds,
  }) {
    _accessToken = accessToken;
    _refreshToken = refreshToken;
    if (expiresInSeconds != null) {
      _accessExpiresAt = DateTime.now().add(Duration(seconds: expiresInSeconds));
    } else {
      _accessExpiresAt = null;
    }
  }

  @override
  void clear() {
    _accessToken = null;
    _refreshToken = null;
    _accessExpiresAt = null;
  }
}
