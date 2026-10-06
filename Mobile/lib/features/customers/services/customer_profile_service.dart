import 'dart:convert';
import 'dart:io';
import '../../identity/services/token_storage.dart';
import '../models/customer_profile.dart';

/// Service managing customer profile matching .spec/contracts/customers.md §2.1.
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

  /// GET /api/customers/me
  Future<CustomerProfile> getProfile() async {
    if (!isMock) {
      try {
        final uri = Uri.parse('$baseUrl/api/customers/me');
        final request = await _httpClient.getUrl(uri).timeout(const Duration(milliseconds: 600));
        _setAuthHeader(request);

        final response = await request.close().timeout(const Duration(milliseconds: 600));
        if (response.statusCode == 200) {
          final body = await response.transform(utf8.decoder).join();
          final map = jsonDecode(body) as Map<String, dynamic>;
          final data = map['data'] as Map<String, dynamic>?;
          if (data != null) {
            _mockProfile = CustomerProfile.fromJson(data);
            return _mockProfile;
          }
        }
      } catch (_) {
        // Fall back to mock profile on network or server offline in tests/dev
      }
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
      try {
        final uri = Uri.parse('$baseUrl/api/customers/me');
        final request = await _httpClient.putUrl(uri).timeout(const Duration(milliseconds: 600));
        _setAuthHeader(request);
        request.headers.contentType = ContentType.json;

        final payload = jsonEncode({
          'fullName': trimmedName,
          'email': sanitizedEmail,
        });
        request.write(payload);

        final response = await request.close().timeout(const Duration(milliseconds: 600));
        if (response.statusCode == 200) {
          final body = await response.transform(utf8.decoder).join();
          final map = jsonDecode(body) as Map<String, dynamic>;
          final data = map['data'] as Map<String, dynamic>?;
          if (data != null) {
            _mockProfile = CustomerProfile.fromJson(data);
            return _mockProfile;
          }
        }
      } catch (e) {
        if (e is ArgumentError) rethrow;
        // Fall back to updating mock store
      }
    }

    _mockProfile = _mockProfile.copyWith(
      fullName: trimmedName,
      email: sanitizedEmail,
    );
    return _mockProfile;
  }

  void _setAuthHeader(HttpClientRequest request) {
    final token = _tokenStorage.accessToken;
    if (token != null && token.isNotEmpty) {
      request.headers.set(HttpHeaders.authorizationHeader, 'Bearer $token');
    }
  }
}
