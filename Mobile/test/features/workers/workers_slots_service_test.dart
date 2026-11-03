import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/auth/token_storage.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/workers/models/booking_slot_models.dart';
import 'package:mobile/features/workers/services/workers_slots_service.dart';

void main() {
  group('WorkersSlotsService over a local server', () {
    late HttpServer server;
    final seen = <({String method, String path, Map<String, dynamic>? body})>[];

    setUp(() async {
      seen.clear();
      TokenStorage().clear();
      TokenStorage().saveTokens(accessToken: 'tok', refreshToken: 'ref');
      server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
      server.listen((HttpRequest request) async {
        final raw = await utf8.decodeStream(request);
        seen.add((
          method: request.method,
          path: request.uri.path,
          body: raw.isEmpty ? null : jsonDecode(raw) as Map<String, dynamic>,
        ));
        request.response.statusCode = 200;
        request.response.headers.contentType = ContentType.json;
        request.response.write(
          jsonEncode({
            'success': true,
            'message': '',
            'data': {
              'slotId': 7,
              'workerId': 1,
              'slotDate': '2026-11-20',
              'shiftCode': 'SHIFT_MORNING',
              'startTime': '08:00',
              'endTime': '12:00',
              'isActive': false,
            },
          }),
        );
        await request.response.close();
      });
    });

    tearDown(() async {
      await server.close(force: true);
      TokenStorage().clear();
    });

    test('toggleSlot PUTs the date and shift in the path and the state in the body', () async {
      final service = WorkersSlotsService(
        apiClient: ApiClient(
          baseUrl: 'http://${server.address.host}:${server.port}',
        ),
      );

      final response = await service.toggleSlot(
        const ToggleBookingSlotRequest(
          slotDate: '2026-11-20',
          shiftCode: 'SHIFT_MORNING',
          isActive: false,
        ),
      );

      expect(seen.single.method, 'PUT');
      expect(
        seen.single.path,
        '/api/v1/workers/me/slots/2026-11-20/SHIFT_MORNING',
      );
      expect(seen.single.body, {'enabled': false});
      expect(response.data!.slotId, 7);
    });
  });
}
