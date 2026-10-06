import 'dart:io';
import '../../../core/models/user_role.dart';
import '../models/auth_models.dart';
import 'api_error.dart';
import 'token_storage.dart';

/// Service handling Customer and Worker OTP authentication flows.
class AuthService {
  final String baseUrl;
  final TokenStorage tokenStorage;
  final HttpClient _client;

  /// Mock mode, only when a caller (a test) passes it explicitly: no network, canned answers.
  /// Off (the default, the real app): every server or network error is reported, never replaced by mock data.
  bool useMockFallback;

  AuthService({
    this.baseUrl = 'http://10.0.2.2:5004', // Android emulator default to localhost
    TokenStorage? tokenStorage,
    HttpClient? client,
    this.useMockFallback = false,
  })  : tokenStorage = tokenStorage ?? TokenStorage(),
        _client = client ?? HttpClient();

  /// Validates and normalizes Vietnamese mobile phone format (0XXXXXXXXX, 10 digits).
  static String? normalizePhone(String raw) {
    var cleaned = raw.replaceAll(RegExp(r'\s+|-|\.'), '');
    if (cleaned.startsWith('+84')) {
      cleaned = '0${cleaned.substring(3)}';
    } else if (cleaned.startsWith('84')) {
      cleaned = '0${cleaned.substring(2)}';
    }

    final regex = RegExp(r'^0[35789]\d{8}$');
    if (!regex.hasMatch(cleaned)) {
      return null;
    }
    return cleaned;
  }

  /// Sends OTP request to /api/auth/otp/request
  Future<OtpRequestResult> requestOtp({
    required String phoneNumber,
    required AppRole role,
  }) async {
    final normalized = normalizePhone(phoneNumber);
    if (normalized == null) {
      throw const FormatException('Số điện thoại không hợp lệ. Vui lòng nhập số di động 10 số tại Việt Nam.');
    }

    if (useMockFallback) {
      return const OtpRequestResult(
        expiresInSeconds: 300,
        resendAvailableInSeconds: 60,
      );
    }

    final roleStr = role == AppRole.worker ? 'Worker' : 'Customer';

    // Throws ApiException on 429 (cooldown, with Retry-After), any other error status, or no connection.
    final envelope = await apiRequest(
      _client,
      'POST',
      Uri.parse('$baseUrl/api/auth/otp/request'),
      body: {'phoneNumber': normalized, 'role': roleStr},
      fallbackMessage: 'Không thể gửi mã OTP. Vui lòng thử lại.',
    );
    return OtpRequestResult.fromJson(envelope['data'] as Map<String, dynamic>);
  }

  /// Verifies OTP code via /api/auth/otp/verify
  Future<VerifyOtpResponse> verifyOtp({
    required String phoneNumber,
    required AppRole role,
    required String code,
  }) async {
    final normalized = normalizePhone(phoneNumber);
    if (normalized == null) {
      return const VerifyOtpResponse(
        errorMessage: 'Số điện thoại không đúng định dạng.',
        statusCode: 400,
      );
    }

    if (code.trim().length != 6 || !RegExp(r'^\d{6}$').hasMatch(code.trim())) {
      return const VerifyOtpResponse(
        errorMessage: 'Mã OTP phải gồm 6 chữ số.',
        statusCode: 400,
      );
    }

    final roleStr = role == AppRole.worker ? 'Worker' : 'Customer';

    if (useMockFallback) {
      // Mock mode (tests only): '000000' is a wrong code, '999999' too many tries, any other 6 digits logs in.
      if (code == '000000') {
        return const VerifyOtpResponse(
          errorMessage: 'Mã xác thực không chính xác hoặc đã hết hạn.',
          statusCode: 401,
        );
      }
      if (code == '999999') {
        return const VerifyOtpResponse(
          errorMessage: 'Bạn đã nhập sai quá số lần quy định. Vui lòng yêu cầu mã mới.',
          statusCode: 429,
        );
      }

      final mockAuth = AuthResult(
        tokenType: 'Bearer',
        accessToken: 'mock_jwt_access_token_demo',
        accessTokenExpiresInSeconds: 900,
        refreshToken: 'mock_refresh_token_demo',
        user: AuthUser(
          id: 101,
          role: roleStr,
          isNewUser: false,
        ),
      );
      tokenStorage.saveAuth(mockAuth);
      return VerifyOtpResponse(authResult: mockAuth);
    }

    RegistrationRequired? reg;
    AuthResult? auth;
    try {
      final envelope = await apiRequest(
        _client,
        'POST',
        Uri.parse('$baseUrl/api/auth/otp/verify'),
        body: {'phoneNumber': normalized, 'role': roleStr, 'code': code.trim()},
        fallbackMessage: 'Xác thực không thành công.',
      );
      final data = envelope['data'] as Map<String, dynamic>;
      if (data.containsKey('registrationToken')) {
        reg = RegistrationRequired.fromJson(data);
      } else {
        auth = AuthResult.fromJson(data);
      }
    } on ApiException catch (e) {
      // 401 wrong/expired code, 429 too many tries, other errors, or status 0 = no connection. Tokens untouched.
      return VerifyOtpResponse(errorMessage: e.message, statusCode: e.statusCode);
    } on TypeError {
      return const VerifyOtpResponse(errorMessage: 'Phản hồi từ máy chủ không hợp lệ.', statusCode: 500);
    }

    if (reg != null) {
      tokenStorage.saveRegistration(reg);
      return VerifyOtpResponse(registrationRequired: reg);
    }
    tokenStorage.saveAuth(auth!);
    return VerifyOtpResponse(authResult: auth);
  }
}
