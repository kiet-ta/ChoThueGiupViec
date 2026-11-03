import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/auth/token_storage.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/workers/services/workers_assignments_service.dart';

// WORKERS-ASSIGNMENT-READ (#442): GET /api/v1/workers/assignments/{id} (contract workers.md 2.3.4).

const _body = {
  'assignmentId': 55,
  'orderId': 155,
  'orderCode': 'GV-55',
  'slotDate': '2026-11-05',
  'shiftCode': 'SHIFT_AFTERNOON',
  'startTime': '13:00',
  'endTime': '17:00',
  'status': 'ASSIGNED',
  'serviceTier': 'PREMIUM',
  'addressLine': '12 Nguyễn Huệ',
  'district': 'Quận 1',
  'city': 'TP. Hồ Chí Minh',
  'latitude': 10.774,
  'longitude': 106.703,
  'payoutAmount': 128000.0,
  'pendingExtension': {
    'extensionId': 12,
    'extraHours': 2.0,
    'extraAmount': 160000.0,
  },
};

void main() {
  group('WorkersAssignmentsService.getAssignment over a local server', () {
    late HttpServer server;
    final seen = <({String method, String path})>[];

    setUp(() async {
      seen.clear();
      TokenStorage().clear();
      TokenStorage().saveTokens(accessToken: 'tok', refreshToken: 'ref');
      server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
      server.listen((HttpRequest request) async {
        seen.add((method: request.method, path: request.uri.path));
        request.response.statusCode = 200;
        request.response.headers.contentType = ContentType.json;
        request.response.write(
          jsonEncode({'success': true, 'message': '', 'data': _body}),
        );
        await request.response.close();
      });
    });

    tearDown(() async {
      await server.close(force: true);
      TokenStorage().clear();
    });

    test('GETs the job by its id and parses the detail', () async {
      final service = WorkersAssignmentsService(
        apiClient: ApiClient(
          baseUrl: 'http://${server.address.host}:${server.port}',
        ),
      );

      final response = await service.getAssignment(55);

      expect(seen.single.method, 'GET');
      expect(seen.single.path, '/api/v1/workers/assignments/55');
      expect(
        response.data!.fullAddress,
        '12 Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh',
      );
      expect(response.data!.pendingExtension!.extensionId, 12);
    });
  });
}
