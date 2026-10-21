import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math';
import 'dart:typed_data';

import '../auth/token_storage.dart';
import 'api_response.dart';

const defaultApiTimeout = Duration(seconds: 15);

/// A file upload carries megabytes over a mobile network, so it gets more time than a JSON call.
const defaultUploadTimeout = Duration(seconds: 60);
const _networkErrorMessage =
    'Không kết nối được máy chủ. Vui lòng kiểm tra mạng và thử lại.';

/// Thrown when an API call fails due to HTTP status (4xx/5xx) or network unreachable.
class ApiException implements Exception {
  final int statusCode;
  final String message;
  final Map<String, List<String>>? fieldErrors;
  final int? retryAfterSeconds;

  /// Machine-readable business code of a failed call (`data.code`, e.g. `FULLY_BOOKED`); null when the server sent none.
  /// Screens branch on this, never on [message].
  final String? code;

  const ApiException(
    this.statusCode,
    this.message, {
    this.fieldErrors,
    this.retryAfterSeconds,
    this.code,
  });

  bool get isNetworkError => statusCode == 0;
  bool get isUnauthorized => statusCode == 401;
  bool get isForbidden => statusCode == 403;
  bool get isNotFound => statusCode == 404;
  bool get isConflict => statusCode == 409;

  @override
  String toString() => message;
}

/// One file part of a `multipart/form-data` request.
class ApiUploadFile {
  final String field;
  final String fileName;
  final String contentType;
  final Uint8List bytes;

  const ApiUploadFile({
    required this.field,
    required this.fileName,
    required this.contentType,
    required this.bytes,
  });
}

/// A built `multipart/form-data` body; kept whole so the 401 refresh retry can send it again.
class _MultipartBody {
  final String boundary;
  final Uint8List bytes;

  const _MultipartBody(this.boundary, this.bytes);

  factory _MultipartBody.build(
    Map<String, String> fields,
    List<ApiUploadFile> files,
  ) {
    final random = Random.secure();
    final boundary =
        '----CommonServiceForm${List.generate(24, (_) => random.nextInt(16).toRadixString(16)).join()}';
    final out = BytesBuilder(copy: false);
    void text(String value) => out.add(utf8.encode(value));

    fields.forEach((name, value) {
      text('--$boundary\r\n');
      text(
        'Content-Disposition: form-data; name="${_quoted(name)}"\r\n\r\n$value\r\n',
      );
    });
    for (final file in files) {
      text('--$boundary\r\n');
      text(
        'Content-Disposition: form-data; name="${_quoted(file.field)}"; filename="${_quoted(file.fileName)}"\r\n'
        'Content-Type: ${file.contentType}\r\n\r\n',
      );
      out.add(file.bytes);
      text('\r\n');
    }
    text('--$boundary--\r\n');
    return _MultipartBody(boundary, out.takeBytes());
  }

  /// A quote, a backslash or a line break would end the header value early.
  static String _quoted(String value) =>
      value.replaceAll(RegExp(r'["\\\r\n]'), '_');
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
  }) : tokenStorage = tokenStorage ?? TokenStorage(),
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

  /// POSTs `multipart/form-data` (the text [fields] and the [files]); the answer is read like any other call.
  Future<ApiResponse<T>> postMultipart<T>(
    String path, {
    Map<String, String> fields = const {},
    required List<ApiUploadFile> files,
    T Function(dynamic)? fromJson,
    Duration timeout = defaultUploadTimeout,
  }) {
    return _sendRequest<T>(
      'POST',
      path,
      multipart: _MultipartBody.build(fields, files),
      fromJson: fromJson,
      timeoutOverride: timeout,
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
    _MultipartBody? multipart,
    Duration? timeoutOverride,
  }) async {
    final uri = _buildUri(path, queryParams);
    final timeout = timeoutOverride ?? this.timeout;

    try {
      final request = await _httpClient.openUrl(method, uri).timeout(timeout);

      // Attach headers
      request.headers.set('Accept', 'application/json');
      if (multipart != null) {
        request.headers.set(
          'Content-Type',
          'multipart/form-data; boundary=${multipart.boundary}',
        );
        request.contentLength = multipart.bytes.length;
      } else if (body != null) {
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
      if (multipart != null) {
        request.add(multipart.bytes);
      } else if (body != null) {
        final encodedBody = utf8.encode(jsonEncode(body));
        request.add(encodedBody);
      }

      final response = await request.close().timeout(timeout);
      final responseBody = await utf8.decodeStream(response).timeout(timeout);
      final statusCode = response.statusCode;

      // Handle 401 token refresh interceptor once
      if (statusCode == 401 &&
          !isRetry &&
          requiresAuth &&
          tokenStorage.hasRefreshToken) {
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
            multipart: multipart,
            timeoutOverride: timeoutOverride,
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

      final isSuccess =
          jsonMap['success'] as bool? ??
          (statusCode >= 200 && statusCode < 300);
      final message = jsonMap['message'] as String? ?? '';

      if (isSuccess && statusCode >= 200 && statusCode < 300) {
        return ApiResponse<T>.fromJson(jsonMap, fromJson);
      }

      // Extract error details on failure
      Map<String, List<String>>? fieldErrors;
      String? code;
      final rawData = jsonMap['data'];
      if (rawData is Map<String, dynamic>) {
        final rawCode = rawData['code'];
        if (rawCode is String && rawCode.isNotEmpty) code = rawCode;
        final rawErrors = rawData['errors'];
        if (rawErrors is Map<String, dynamic>) {
          fieldErrors = rawErrors.map(
            (k, v) =>
                MapEntry(k, (v as List).map((e) => e.toString()).toList()),
          );
        }
      }

      int? retryAfterSeconds;
      final retryHeader = response.headers.value('retry-after');
      if (retryHeader != null) {
        retryAfterSeconds = int.tryParse(retryHeader);
      }

      throw ApiException(
        statusCode,
        message.isNotEmpty
            ? message
            : 'Yêu cầu không thành công (mã $statusCode).',
        fieldErrors: fieldErrors,
        retryAfterSeconds: retryAfterSeconds,
        code: code,
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

  /// The full address of an API [path] such as `/api/workers/assignments/1/photos/2/file`.
  String resolveUrl(String path) => _buildUri(path).toString();

  /// The `Authorization` header of the signed-in person (empty when signed out), for a file that only an API route serves.
  Map<String, String> authHeaders() {
    final token = tokenStorage.accessToken;
    return token == null ? const {} : {'Authorization': 'Bearer $token'};
  }

  Future<bool> _tryRefreshToken() async {
    final refreshToken = tokenStorage.refreshToken;
    if (refreshToken == null) return false;

    try {
      final refreshUri = _buildUri('/api/auth/refresh');
      final request = await _httpClient
          .openUrl('POST', refreshUri)
          .timeout(timeout);
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
