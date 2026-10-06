import 'dart:io';
import '../../identity/services/api_error.dart';
import '../../identity/services/token_storage.dart';
import '../models/customer_profile.dart';

/// Service managing customer profile matching .spec/contracts/customers.md §2.1.
///
/// [isMock] (only when a caller, a test, passes it): in-memory profile, no network.
/// Off (the default, the real app): server or network errors are thrown as [ApiException], never hidden.
class CustomerProfileService {
  final String baseUrl;
  final HttpClient _httpClient;
  final TokenStorage _tokenStorage;
  final bool isMock;

  CustomerProfile _mockProfile;

  CustomerProfileService({
    String? baseUrl,
    HttpClient? httpClient,
    TokenStorage? tokenStorage,
    this.isMock = false,
  })  : baseUrl = baseUrl ?? 'http://10.0.2.2:5004',
        _httpClient = httpClient ?? HttpClient(),
        _tokenStorage = tokenStorage ?? TokenStorage(),
        _mockProfile = CustomerProfile(
          customerId: 1,
          phoneNumber: '0901234567',
          fullName: 'Nguyễn Văn An',
          email: 'an.nguyen@example.com',
          trustScore: 5.0,
          accountStatus: 'ACTIVE',
          createdAt: DateTime(2026, 1, 15),
        );

  CustomerProfile get mockProfile => _mockProfile;

  void setMockProfile(CustomerProfile profile) {
    _mockProfile = profile;
  }

  Future<Map<String, dynamic>> _call(String method, String fallbackMessage, {Object? body}) => apiRequest(
        _httpClient,
        method,
        Uri.parse('$baseUrl/api/customers/me'),
        bearerToken: _tokenStorage.accessToken,
        body: body,
        fallbackMessage: fallbackMessage,
      );

  /// GET /api/customers/me
  Future<CustomerProfile> getProfile() async {
    if (!isMock) {
      final envelope = await _call('GET', 'Không tải được hồ sơ.');
      return CustomerProfile.fromJson(envelope['data'] as Map<String, dynamic>);
    }
    return _mockProfile;
  }

  /// PUT /api/customers/me
  Future<CustomerProfile> updateProfile({
    required String fullName,
    String? email,
  }) async {
    final trimmedName = fullName.trim();
    if (trimmedName.isEmpty) {
      throw ArgumentError('Họ và tên không được để trống.');
    }
    if (trimmedName.length > 100) {
      throw ArgumentError('Họ và tên không được vượt quá 100 ký tự.');
    }

    final trimmedEmail = email?.trim();
    if (trimmedEmail != null && trimmedEmail.isNotEmpty) {
      if (trimmedEmail.length > 255) {
        throw ArgumentError('Email không được vượt quá 255 ký tự.');
      }
      final emailRegex = RegExp(r'^[^@\s]+@[^@\s]+\.[^@\s]+$');
      if (!emailRegex.hasMatch(trimmedEmail)) {
        throw ArgumentError('Email không đúng định dạng.');
      }
    }

    final sanitizedEmail = (trimmedEmail != null && trimmedEmail.isNotEmpty) ? trimmedEmail : null;

    if (!isMock) {
      final envelope = await _call(
        'PUT',
        'Cập nhật hồ sơ thất bại.',
        body: {'fullName': trimmedName, 'email': sanitizedEmail},
      );
      return CustomerProfile.fromJson(envelope['data'] as Map<String, dynamic>);
    }

    _mockProfile = _mockProfile.copyWith(
      fullName: trimmedName,
      email: sanitizedEmail,
    );
    return _mockProfile;
  }
}
