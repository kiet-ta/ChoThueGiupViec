// Data models for the Identity module per .spec/contracts/identity.md.

class OtpRequestResult {
  final int expiresInSeconds;
  final int resendAvailableInSeconds;

  const OtpRequestResult({
    required this.expiresInSeconds,
    required this.resendAvailableInSeconds,
  });

  factory OtpRequestResult.fromJson(Map<String, dynamic> json) {
    return OtpRequestResult(
      expiresInSeconds: json['expiresInSeconds'] as int? ?? 300,
      resendAvailableInSeconds: json['resendAvailableInSeconds'] as int? ?? 60,
    );
  }
}

class AuthUser {
  final int id;
  final String role;
  final bool isNewUser;

  const AuthUser({
    required this.id,
    required this.role,
    required this.isNewUser,
  });

  factory AuthUser.fromJson(Map<String, dynamic> json) {
    return AuthUser(
      id: json['id'] as int? ?? 0,
      role: json['role'] as String? ?? 'Customer',
      isNewUser: json['isNewUser'] as bool? ?? false,
    );
  }

  Map<String, dynamic> toJson() => {
    'id': id,
    'role': role,
    'isNewUser': isNewUser,
  };
}

class AuthResult {
  final String tokenType;
  final String accessToken;
  final int accessTokenExpiresInSeconds;
  final String refreshToken;
  final AuthUser user;

  const AuthResult({
    required this.tokenType,
    required this.accessToken,
    required this.accessTokenExpiresInSeconds,
    required this.refreshToken,
    required this.user,
  });

  factory AuthResult.fromJson(Map<String, dynamic> json) {
    return AuthResult(
      tokenType: json['tokenType'] as String? ?? 'Bearer',
      accessToken: json['accessToken'] as String? ?? '',
      accessTokenExpiresInSeconds: json['accessTokenExpiresInSeconds'] as int? ?? 900,
      refreshToken: json['refreshToken'] as String? ?? '',
      user: AuthUser.fromJson(json['user'] as Map<String, dynamic>? ?? {}),
    );
  }
}

class RegistrationRequired {
  final bool isNewUser;
  final String registrationToken;
  final int registrationTokenExpiresInSeconds;

  const RegistrationRequired({
    required this.isNewUser,
    required this.registrationToken,
    required this.registrationTokenExpiresInSeconds,
  });

  factory RegistrationRequired.fromJson(Map<String, dynamic> json) {
    return RegistrationRequired(
      isNewUser: json['isNewUser'] as bool? ?? true,
      registrationToken: json['registrationToken'] as String? ?? '',
      registrationTokenExpiresInSeconds: json['registrationTokenExpiresInSeconds'] as int? ?? 1800,
    );
  }
}

class VerifyOtpResponse {
  final AuthResult? authResult;
  final RegistrationRequired? registrationRequired;
  final String? errorMessage;
  final int? statusCode;

  const VerifyOtpResponse({
    this.authResult,
    this.registrationRequired,
    this.errorMessage,
    this.statusCode,
  });

  bool get isSuccess => errorMessage == null && (authResult != null || registrationRequired != null);
  bool get isNewWorker => registrationRequired != null;
}
