import 'dart:async';

import 'package:signalr_netcore/signalr_client.dart';

import '../auth/token_storage.dart';
import '../network/api_config.dart';

/// One realtime message from the backend hub (`NotificationMessageDto`, decisions Q07 / D36): the screens tell messages apart by
/// [topic], e.g. `dispatch.auto_accepted` (D33) with `data.assignmentId` and `data.orderId`.
class RealtimeNotification {
  final String topic;
  final String title;
  final String body;
  final Map<String, String> data;

  const RealtimeNotification({
    required this.topic,
    required this.title,
    required this.body,
    this.data = const {},
  });

  /// Reads the hub's JSON (camelCase). Null when the message is not a notification object.
  static RealtimeNotification? fromHub(Object? raw) {
    if (raw is! Map) return null;
    final topic = raw['topic'];
    if (topic is! String || topic.isEmpty) return null;
    final data = <String, String>{};
    final rawData = raw['data'];
    if (rawData is Map) {
      rawData.forEach((key, value) {
        if (key != null && value != null) data['$key'] = '$value';
      });
    }
    return RealtimeNotification(
      topic: topic,
      title: '${raw['title'] ?? ''}',
      body: '${raw['body'] ?? ''}',
      data: data,
    );
  }
}

/// Topics the app reacts to.
class RealtimeTopics {
  RealtimeTopics._();

  /// A freelancer with "Tự động nhận đơn" on was given a job (decisions D33).
  static const autoAccepted = 'dispatch.auto_accepted';
}

/// The few calls the service makes on a hub connection, so tests can pass a fake instead of a socket.
abstract class IHubConnection {
  Future<void> start();
  Future<void> stop();
  void on(String method, void Function(List<Object?>? arguments) handler);
  void onClose(void Function() handler);
}

typedef HubConnectionFactory = IHubConnection Function(
  String url,
  Future<String> Function() token,
);

/// The realtime channel of a signed-in user (MOB-RT-01, #893, decisions D37).
abstract class IRealtimeService {
  /// The messages received while connected. Broadcast: several screens may listen.
  Stream<RealtimeNotification> get notifications;

  /// Connects when a session exists; a failure is retried later and never thrown (the screens keep polling meanwhile).
  Future<void> connect();

  /// Stops the connection and any retry (sign-out). [connect] may be called again afterwards.
  Future<void> disconnect();

  /// Releases the stream; the service cannot be used afterwards.
  Future<void> dispose();
}

/// SignalR client of `<api base>/hubs/notifications`. The token is read from [ITokenStorage] at every (re)connect, so a refreshed
/// token is used; the server takes it from `?access_token=` (D37) and puts the connection in its own `role:id` group (D36).
/// The package retries a lost connection a few times (0, 2, 10, 30 s); after that, and after a failed start, this service tries
/// again every [retryDelay] until [disconnect].
class RealtimeService implements IRealtimeService {
  /// The hub path mapped by the backend (`RealtimeModule`).
  static const hubPath = '/hubs/notifications';

  /// The hub's client method (`INotificationClient.ReceiveNotification`).
  static const clientMethod = 'ReceiveNotification';

  final ITokenStorage _tokens;
  final String baseUrl;
  final HubConnectionFactory _factory;
  final Duration retryDelay;

  final _controller = StreamController<RealtimeNotification>.broadcast();
  IHubConnection? _connection;
  Timer? _retry;
  bool _wanted = false;

  RealtimeService({
    ITokenStorage? tokens,
    this.baseUrl = apiBaseUrl,
    HubConnectionFactory? connectionFactory,
    this.retryDelay = const Duration(seconds: 30),
  }) : _tokens = tokens ?? TokenStorage(),
       _factory = connectionFactory ?? _signalR;

  @override
  Stream<RealtimeNotification> get notifications => _controller.stream;

  /// The hub URL for [baseUrl] (a trailing slash is ignored).
  static String hubUrl(String baseUrl) =>
      '${baseUrl.endsWith('/') ? baseUrl.substring(0, baseUrl.length - 1) : baseUrl}$hubPath';

  @override
  Future<void> connect() async {
    _wanted = true;
    if (_connection != null || _controller.isClosed) return;
    final token = _tokens.accessToken;
    // No session: nothing to listen to.
    if (token == null || token.isEmpty) return;

    final connection = _factory(
      hubUrl(baseUrl),
      () async => _tokens.accessToken ?? '',
    );
    connection.on(clientMethod, _receive);
    connection.onClose(() {
      if (identical(_connection, connection)) {
        _connection = null;
        _scheduleRetry();
      }
    });
    _connection = connection;
    try {
      await connection.start();
    } catch (_) {
      // Hub down, no network, refused token: the screens keep polling; try again later.
      if (identical(_connection, connection)) _connection = null;
      _scheduleRetry();
    }
  }

  @override
  Future<void> disconnect() async {
    _wanted = false;
    _retry?.cancel();
    _retry = null;
    final connection = _connection;
    _connection = null;
    if (connection == null) return;
    try {
      await connection.stop();
    } catch (_) {
      // Already closed.
    }
  }

  @override
  Future<void> dispose() async {
    await disconnect();
    await _controller.close();
  }

  void _receive(List<Object?>? arguments) {
    if (arguments == null || arguments.isEmpty || _controller.isClosed) return;
    final message = RealtimeNotification.fromHub(arguments.first);
    if (message != null) _controller.add(message);
  }

  void _scheduleRetry() {
    if (!_wanted || _controller.isClosed || _retry != null) return;
    _retry = Timer(retryDelay, () {
      _retry = null;
      if (_wanted) connect();
    });
  }

  static IHubConnection _signalR(String url, Future<String> Function() token) =>
      _SignalRHubConnection(
        HubConnectionBuilder()
            .withUrl(
              url,
              options: HttpConnectionOptions(accessTokenFactory: token),
            )
            .withAutomaticReconnect()
            .build(),
      );
}

class _SignalRHubConnection implements IHubConnection {
  final HubConnection _hub;

  _SignalRHubConnection(this._hub);

  @override
  Future<void> start() => _hub.start() ?? Future.value();

  @override
  Future<void> stop() => _hub.stop();

  @override
  void on(String method, void Function(List<Object?>? arguments) handler) =>
      _hub.on(method, handler);

  @override
  void onClose(void Function() handler) =>
      _hub.onclose(({Exception? error}) => handler());
}
