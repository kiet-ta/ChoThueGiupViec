import 'dart:convert';
import 'dart:io';

/// Scripted backend on loopback for HTTP tests (dart:io only, no package).
/// Each route answers a fixed status, body (JSON-encoded unless it is a String) and optional headers.
class FakeBackend {
  late final HttpServer _server;
  final _routes = <String, ({int status, Object? body, Map<String, String> headers})>{};

  /// `Authorization` header of every request, keyed by 'METHOD /path'.
  final authHeaders = <String, String?>{};

  /// Decoded JSON body of every request, keyed by 'METHOD /path'.
  final requestBodies = <String, Object?>{};

  String get baseUrl => 'http://127.0.0.1:${_server.port}';

  /// A base URL where nothing listens (connection refused).
  static const unreachableBaseUrl = 'http://127.0.0.1:1';

  Future<void> start() async {
    _server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
    _server.listen((req) async {
      final key = '${req.method} ${req.uri.path}';
      authHeaders[key] = req.headers.value(HttpHeaders.authorizationHeader);
      final raw = await utf8.decoder.bind(req).join();
      requestBodies[key] = raw.isEmpty ? null : jsonDecode(raw);

      final route = _routes[key];
      req.response.statusCode = route?.status ?? 404;
      route?.headers.forEach(req.response.headers.set);
      final body = route?.body;
      if (body != null) {
        req.response.headers.contentType = body is String ? ContentType.html : ContentType.json;
        req.response.write(body is String ? body : jsonEncode(body));
      }
      await req.response.close();
    });
  }

  void on(String method, String path, int status, [Object? body, Map<String, String> headers = const {}]) {
    _routes['$method $path'] = (status: status, body: body, headers: headers);
  }

  Future<void> stop() => _server.close(force: true);

  /// Success envelope `ApiResponse{success,message,data}`.
  static Map<String, Object?> ok(Object? data) => {'success': true, 'message': 'OK', 'data': data};

  /// Error envelope.
  static Map<String, Object?> error(String message, [Object? data]) =>
      {'success': false, 'message': message, 'data': data};
}
