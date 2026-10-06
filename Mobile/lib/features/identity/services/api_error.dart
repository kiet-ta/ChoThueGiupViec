import 'dart:async';
import 'dart:convert';
import 'dart:io';

/// Timeout for one real backend call (connect, then response). Phone networks are slow; 600 ms was not enough.
const apiTimeout = Duration(seconds: 15);

/// Error of a backend call: a non-2xx answer (envelope `ApiResponse{success,message,data}`) or no answer at all.
/// [statusCode] is 0 when the server could not be reached. `toString()` is the message, so screens can show it as is.
class ApiException implements Exception {
  final int statusCode;
  final String message;

  /// `data.errors` of a 400 (decision O5): field -> messages.
  final Map<String, List<String>>? fieldErrors;

  /// `Retry-After` of a 423/429, in seconds.
  final int? retryAfterSeconds;

  const ApiException(this.statusCode, this.message, {this.fieldErrors, this.retryAfterSeconds});

  bool get isNetworkError => statusCode == 0;

  @override
  String toString() => message;
}

const _networkMessage = 'Không kết nối được máy chủ. Vui lòng kiểm tra mạng và thử lại.';

/// Runs one backend call; a network failure or timeout becomes [ApiException] with status 0.
/// An [ApiException] thrown inside (from [readEnvelope]) passes through unchanged.
Future<T> guardNetwork<T>(Future<T> Function() call) async {
  try {
    return await call();
  } on ApiException {
    rethrow;
  } on SocketException {
    throw const ApiException(0, _networkMessage);
  } on TimeoutException {
    throw const ApiException(0, _networkMessage);
  } on HttpException {
    throw const ApiException(0, _networkMessage);
  } on HandshakeException {
    throw const ApiException(0, _networkMessage);
  }
}

/// One JSON call: [method] [uri] with an optional Bearer token and JSON [body]; returns the 2xx envelope,
/// throws [ApiException] for an error status or no connection.
Future<Map<String, dynamic>> apiRequest(
  HttpClient client,
  String method,
  Uri uri, {
  String? bearerToken,
  Object? body,
  required String fallbackMessage,
}) {
  return guardNetwork(() async {
    final request = await client.openUrl(method, uri).timeout(apiTimeout);
    if (bearerToken != null && bearerToken.isNotEmpty) {
      request.headers.set(HttpHeaders.authorizationHeader, 'Bearer $bearerToken');
    }
    if (body != null) {
      request.headers.contentType = ContentType.json;
      request.write(jsonEncode(body));
    }
    final response = await request.close().timeout(apiTimeout);
    return readEnvelope(response, fallbackMessage: fallbackMessage);
  });
}

/// Reads the response envelope. Returns it for a 2xx with `success != false`; otherwise throws [ApiException]
/// with the status and the envelope `message` (or [fallbackMessage] when the body is not a JSON envelope).
Future<Map<String, dynamic>> readEnvelope(HttpClientResponse response, {required String fallbackMessage}) async {
  final status = response.statusCode;
  final body = await response.transform(utf8.decoder).join();

  Map<String, dynamic>? envelope;
  try {
    final decoded = body.isEmpty ? null : jsonDecode(body);
    if (decoded is Map<String, dynamic>) envelope = decoded;
  } on FormatException {
    envelope = null;
  }

  final ok = status >= 200 && status < 300;
  if (ok && envelope != null && envelope['success'] != false) return envelope;
  if (ok && envelope == null && body.isEmpty) return const {'success': true, 'data': null};

  final serverMessage = envelope?['message'];
  final message = serverMessage is String && serverMessage.trim().isNotEmpty
      ? serverMessage
      : ok
          ? '$fallbackMessage (phản hồi không hợp lệ)'
          : '$fallbackMessage (mã lỗi $status)';

  throw ApiException(
    status,
    message,
    fieldErrors: _fieldErrors(envelope),
    retryAfterSeconds: int.tryParse(response.headers.value(HttpHeaders.retryAfterHeader) ?? ''),
  );
}

Map<String, List<String>>? _fieldErrors(Map<String, dynamic>? envelope) {
  final data = envelope?['data'];
  final errors = data is Map<String, dynamic> ? data['errors'] : null;
  if (errors is! Map<String, dynamic>) return null;
  return errors.map((field, messages) => MapEntry(
        field,
        messages is List ? messages.map((m) => m.toString()).toList() : [messages.toString()],
      ));
}
