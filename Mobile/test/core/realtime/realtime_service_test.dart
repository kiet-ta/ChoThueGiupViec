import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/auth/token_storage.dart';
import 'package:mobile/core/realtime/realtime_service.dart';

/// MOB-RT-01 (#893), decisions D37: the realtime service against a fake hub connection.
class FakeHubConnection implements IHubConnection {
  final String url;
  final Future<String> Function() token;
  final handlers = <String, void Function(List<Object?>?)>{};
  void Function()? closed;
  int starts = 0;
  int stops = 0;
  bool failStart = false;

  FakeHubConnection(this.url, this.token);

  @override
  Future<void> start() async {
    starts++;
    if (failStart) throw Exception('hub down');
  }

  @override
  Future<void> stop() async => stops++;

  @override
  void on(String method, void Function(List<Object?>? arguments) handler) =>
      handlers[method] = handler;

  @override
  void onClose(void Function() handler) => closed = handler;

  void push(Object? message) =>
      handlers[RealtimeService.clientMethod]!([message]);
}

class _Tokens implements ITokenStorage {
  @override
  String? accessToken;

  @override
  String? get refreshToken => null;

  @override
  bool get isAuthenticated => accessToken != null;

  @override
  bool get isAccessTokenExpired => false;

  @override
  void saveTokens({
    required String accessToken,
    required String refreshToken,
    int? expiresInSeconds,
  }) => this.accessToken = accessToken;

  @override
  void clear() => accessToken = null;
}

void main() {
  late _Tokens tokens;
  late List<FakeHubConnection> made;
  var failNext = false;

  RealtimeService service({Duration retry = const Duration(seconds: 30)}) =>
      RealtimeService(
        tokens: tokens,
        baseUrl: 'http://10.0.2.2:5004/',
        retryDelay: retry,
        connectionFactory: (url, token) {
          final c = FakeHubConnection(url, token)..failStart = failNext;
          made.add(c);
          return c;
        },
      );

  setUp(() {
    tokens = _Tokens()..accessToken = 'token-1';
    made = [];
    failNext = false;
  });

  test(
    'connects to the hub with the stored token and streams the messages',
    () async {
      final s = service();
      final received = <RealtimeNotification>[];
      s.notifications.listen(received.add);

      await s.connect();

      final c = made.single;
      expect(c.url, 'http://10.0.2.2:5004/hubs/notifications');
      expect(c.starts, 1);
      tokens.accessToken =
          'token-2'; // a refreshed token is read at each (re)connect
      expect(await c.token(), 'token-2');

      c.push({
        'recipientRole': 'Worker',
        'recipientId': 55,
        'topic': 'dispatch.auto_accepted',
        'title': 'Đã tự nhận việc',
        'body': 'Bạn có việc mới lúc 08:00.',
        'data': {'assignmentId': '42', 'orderId': 7},
      });
      c.push('not a notification');
      c.push({'title': 'no topic'});
      await pumpEventQueue();

      final notice = received.single;
      expect(notice.topic, RealtimeTopics.autoAccepted);
      expect(notice.title, 'Đã tự nhận việc');
      expect(notice.body, 'Bạn có việc mới lúc 08:00.');
      expect(notice.data, {'assignmentId': '42', 'orderId': '7'});
      await s.dispose();
    },
  );

  test('no session: no connection', () async {
    tokens.accessToken = null;
    final s = service();
    await s.connect();
    expect(made, isEmpty);
    await s.dispose();
  });

  test(
    'a failed start is swallowed and retried later; disconnect stops the retry',
    () async {
      failNext = true;
      final s = service(retry: const Duration(milliseconds: 10));

      await s.connect(); // does not throw: the screens keep polling
      expect(made, hasLength(1));

      failNext = false;
      await Future<void>.delayed(const Duration(milliseconds: 30));
      expect(made, hasLength(2));
      expect(made.last.starts, 1);

      await s.disconnect();
      expect(made.last.stops, 1);
      made.last.closed!(); // a close after sign-out does not reconnect
      await Future<void>.delayed(const Duration(milliseconds: 30));
      expect(made, hasLength(2));
      await s.dispose();
    },
  );

  test('a connection closed by the server (package retries exhausted) is opened again', () async {
    final s = service(retry: const Duration(milliseconds: 10));
    await s.connect();
    made.single.closed!();

    await Future<void>.delayed(const Duration(milliseconds: 30));
    expect(made, hasLength(2));
    await s.dispose();
    expect(made.last.stops, 1);
  });

  test('connect twice keeps one connection', () async {
    final s = service();
    await s.connect();
    await s.connect();
    expect(made, hasLength(1));
    await s.dispose();
  });
}
