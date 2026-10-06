import 'dart:async';
import 'dart:convert';
import 'dart:io';

import '../auth/token_storage.dart';
import 'api_response.dart';

const defaultApiTimeout = Duration(seconds: 15);
const _networkErrorMessage = 'Không kết nối được máy chủ. Vui lòng kiểm tra mạng và thử lại.';

/// Thrown when an API call fails due to HTTP status (4xx/5xx) or network unreachable.
class ApiException implements Exception {
  final int statusCode;
  final String message;
  final Map<String, List<String>>? fieldErrors;
  final int? retryAfterSeconds;

  const ApiException(
    this.statusCode,
    this.message, {
    this.fieldErrors,
    this.retryAfterSeconds,
  });

  bool get isNetworkError => statusCode == 0;
  bool get isUnauthorized => statusCode == 401;
  bool get isForbidden => statusCode == 403;
  bool get isNotFound => statusCode == 404;
  bool get isConflict => statusCode == 409;

  @override
  String toString() => message;
}

/// Central typed HTTP client for all Mobile features.
/// Handles ApiResponse envelope wrapping, token headers, error translation, and retry.
class ApiClient {
  final String baseUrl;
  final TokenStorage tokenStorage;
  final HttpClient _httpClient;
  final Duration timeout;

  ApiClient({
    this.baseUrl = 'http://10.0.2.2:5004',
    TokenStorage? tokenStorage,
    HttpClient? httpClient,
    this.timeout = defaultApiTimeout,
  })  : tokenStorage = tokenStorage ?? TokenStorage(),
        _httpClient = httpClient ?? HttpClient();

  Uri _buildUri(String path, [Map<String, String>? queryParams]) {
    final cleanPath = path.startsWith('/') ? path : '/$path';
    final fullUrl = '$baseUrl$cleanPath';
    final baseUri = Uri.parse(fullUrl);
    if (queryParams == null || queryParams.isEmpty) {
      return baseUri;
    }
    return baseUri.replace(queryParameters: queryParams);
  }

  Future<ApiResponse<T>> get<T>(
    String path, {
    Map<String, String>? queryParams,
    T Function(dynamic)? fromJson,
    bool requiresAuth = true,
  }) {
    return _sendRequest<T>(
      'GET',
      path,
      queryParams: queryParams,
      fromJson: fromJson,
      requiresAuth: requiresAuth,
    );
  }

  Future<ApiResponse<T>> post<T>(
    String path, {
    Object? body,
    Map<String, String>? queryParams,
    T Function(dynamic)? fromJson,
    bool requiresAuth = true,
  }) {
    return _sendRequest<T>(
      'POST',
      path,
      body: body,
      queryParams: queryParams,
      fromJson: fromJson,
      requiresAuth: requiresAuth,
    );
  }

  Future<ApiResponse<T>> put<T>(
    String path, {
    Object? body,
    Map<String, String>? queryParams,
    T Function(dynamic)? fromJson,
    bool requiresAuth = true,
  }) {
    return _sendRequest<T>(
      'PUT',
      path,
      body: body,
      queryParams: queryParams,
      fromJson: fromJson,
      requiresAuth: requiresAuth,
    );
  }

  Future<ApiResponse<T>> delete<T>(
    String path, {
    Map<String, String>? queryParams,
    T Function(dynamic)? fromJson,
    bool requiresAuth = true,
  }) {
    return _sendRequest<T>(
      'DELETE',
      path,
      queryParams: queryParams,
      fromJson: fromJson,
      requiresAuth: requiresAuth,
    );
  }

  Future<ApiResponse<T>> _sendRequest<T>(
    String method,
    String path, {
    Object? body,
    Map<String, String>? queryParams,
    T Function(dynamic)? fromJson,
    bool requiresAuth = true,
    bool isRetry = false,
  }) async {
    final uri = _buildUri(path, queryParams);

    try {
      final request = await _httpClient.openUrl(method, uri).timeout(timeout);

      // Attach headers
      request.headers.set('Accept', 'application/json');
      if (body != null) {
        request.headers.set('Content-Type', 'application/json; charset=utf-8');
      }

      // Attach Bearer token if required and available
      if (requiresAuth && tokenStorage.accessToken != null) {
        request.headers.set(
          'Authorization',
          'Bearer ${tokenStorage.accessToken}',
        );
      }

      // Write request body
      if (body != null) {
        final encodedBody = utf8.encode(jsonEncode(body));
        request.add(encodedBody);
      }

      final response = await request.close().timeout(timeout);
      final responseBody = await utf8.decodeStream(response).timeout(timeout);
      final statusCode = response.statusCode;

      // Handle 401 token refresh interceptor once
      if (statusCode == 401 && !isRetry && requiresAuth && tokenStorage.hasRefreshToken) {
        final refreshed = await _tryRefreshToken();
        if (refreshed) {
          return await _sendRequest<T>(
            method,
            path,
            body: body,
            queryParams: queryParams,
            fromJson: fromJson,
            requiresAuth: requiresAuth,
            isRetry: true,
          );
        }
      }

      // Parse JSON envelope
      Map<String, dynamic> jsonMap;
      try {
        jsonMap = jsonDecode(responseBody) as Map<String, dynamic>;
      } catch (_) {
        if (statusCode >= 200 && statusCode < 300) {
          return ApiResponse<T>(success: true, message: 'OK');
        }
        throw ApiException(
          statusCode,
          'Máy chủ trả về kết quả không hợp lệ (mã $statusCode).',
        );
      }

      final isSuccess = jsonMap['success'] as bool? ?? (statusCode >= 200 && statusCode < 300);
      final message = jsonMap['message'] as String? ?? '';

      if (isSuccess && statusCode >= 200 && statusCode < 300) {
        return ApiResponse<T>.fromJson(jsonMap, fromJson);
      }

      // Extract error details on failure
      Map<String, List<String>>? fieldErrors;
      final rawData = jsonMap['data'];
      if (rawData is Map<String, dynamic>) {
        final rawErrors = rawData['errors'];
        if (rawErrors is Map<String, dynamic>) {
          fieldErrors = rawErrors.map((k, v) => MapEntry(
                k,
                (v as List).map((e) => e.toString()).toList(),
              ));
        }
      }

      int? retryAfterSeconds;
      final retryHeader = response.headers.value('retry-after');
      if (retryHeader != null) {
        retryAfterSeconds = int.tryParse(retryHeader);
      }

      throw ApiException(
        statusCode,
        message.isNotEmpty ? message : 'Yêu cầu không thành công (mã $statusCode).',
        fieldErrors: fieldErrors,
        retryAfterSeconds: retryAfterSeconds,
      );
    } on ApiException {
      rethrow;
    } on SocketException {
      throw const ApiException(0, _networkErrorMessage);
    } on TimeoutException {
      throw const ApiException(0, _networkErrorMessage);
    } on HttpException {
      throw const ApiException(0, _networkErrorMessage);
    } on HandshakeException {
      throw const ApiException(0, _networkErrorMessage);
    } catch (e) {
      throw ApiException(0, 'Lỗi kết nối: ${e.toString()}');
    }
  }

  Future<bool> _tryRefreshToken() async {
    final refreshToken = tokenStorage.refreshToken;
    if (refreshToken == null) return false;

    try {
      final refreshUri = _buildUri('/api/auth/refresh');
      final request = await _httpClient.openUrl('POST', refreshUri).timeout(timeout);
      request.headers.set('Content-Type', 'application/json; charset=utf-8');
      request.headers.set('Accept', 'application/json');
      request.add(utf8.encode(jsonEncode({'refreshToken': refreshToken})));

      final response = await request.close().timeout(timeout);
      if (response.statusCode >= 200 && response.statusCode < 300) {
        final bodyStr = await utf8.decodeStream(response).timeout(timeout);
        final map = jsonDecode(bodyStr) as Map<String, dynamic>;
        final data = map['data'] as Map<String, dynamic>?;
        if (data != null && data['accessToken'] is String) {
          tokenStorage.saveTokens(
            accessToken: data['accessToken'] as String,
            refreshToken: data['refreshToken'] as String? ?? refreshToken,
            expiresInSeconds: data['expiresInSeconds'] as int?,
          );
          return true;
        }
      }
    } catch (_) {
      // Refresh failed
    }

    tokenStorage.clear();
    return false;
  }
}
