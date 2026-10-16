import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/auth/token_storage.dart';
import 'package:mobile/core/device/location_wrapper.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/workers/services/workers_location_service.dart';

void main() {
  group('WorkersLocationService', () {
    late HttpServer server;
    late WorkersLocationService service;
    late TokenStorage storage;
    final requests = <Map<String, dynamic>>[];
    final methods = <String>[];
    final paths = <String>[];
    final auth = <String?>[];

    setUp(() async {
      requests.clear();
      methods.clear();
      paths.clear();
      auth.clear();
      storage = TokenStorage();
      storage.clear();
      storage.saveTokens(accessToken: 'worker_token', refreshToken: 'ref');

      server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
      server.listen((HttpRequest request) async {
        methods.add(request.method);
        paths.add(request.uri.path);
        auth.add(request.headers.value('authorization'));
        requests.add(
          jsonDecode(await utf8.decodeStream(request)) as Map<String, dynamic>,
        );
        request.response.statusCode = 200;
        request.response.headers.contentType = ContentType.json;
        request.response.write(
          jsonEncode({
            'success': true,
            'message': 'Location saved.',
            'data': {'latitude': 10.7765, 'longitude': 106.7005},
          }),
        );
        await request.response.close();
      });

      service = WorkersLocationService(
        apiClient: ApiClient(
          baseUrl: 'http://${server.address.host}:${server.port}',
          tokenStorage: storage,
        ),
        locationWrapper: LocationWrapper(
          mockLocation: GeoPoint(
            latitude: 10.7765,
            longitude: 106.7005,
            timestamp: DateTime.utc(2026, 10, 16),
          ),
        ),
      );
    });

    tearDown(() async {
      await server.close(force: true);
    });

    test(
      'shares the device position with PUT /api/workers/me/location',
      () async {
        final res = await service.shareCurrentLocation();

        expect(methods, ['PUT']);
        expect(paths, ['/api/workers/me/location']);
        expect(auth, ['Bearer worker_token']);
        expect(requests.single, {'latitude': 10.7765, 'longitude': 106.7005});
        expect(res.success, isTrue);
        expect(res.data!.latitude, 10.7765);
        expect(res.data!.longitude, 106.7005);
      },
    );

    test('updateLocation sends exactly the given coordinates', () async {
      await service.updateLocation(latitude: 21.0285, longitude: 105.8542);

      expect(requests.single, {'latitude': 21.0285, 'longitude': 105.8542});
    });
  });
}
