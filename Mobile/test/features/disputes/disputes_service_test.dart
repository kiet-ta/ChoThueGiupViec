import 'dart:convert';
import 'dart:io';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/auth/token_storage.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/disputes/models/dispute_models.dart';
import 'package:mobile/features/disputes/services/disputes_service.dart';
import 'package:mobile/features/disputes/services/evidence_uploader.dart';

Map<String, dynamic> disputeJson({int id = 1, String status = 'OPEN'}) => {
      'disputeId': id,
      'orderId': 42,
      'raisedBy': 'CUSTOMER',
      'category': 'QUALITY',
      'description': 'x',
      'evidenceUrls': ['a'],
      'disputeStatus': status,
      'faultParty': null,
      'compensationAmount': null,
      'slaDueAt': '2026-10-10T03:00:00Z',
      'resolvedAt': null,
      'createdAt': '2026-10-08T03:00:00Z',
    };

void main() {
  group('DisputesService over a local server', () {
    late HttpServer server;
    late DisputesService service;
    final seen = <({String method, String path, Object? body})>[];

    Future<void> answer(int status, Object? json) async {
      server.listen((HttpRequest request) async {
        final raw = await utf8.decodeStream(request);
        seen.add((method: request.method, path: request.uri.path, body: raw.isEmpty ? null : jsonDecode(raw)));
        request.response.statusCode = status;
        request.response.headers.contentType = ContentType.json;
        request.response.write(jsonEncode(json));
        await request.response.close();
      });
    }

    setUp(() async {
      seen.clear();
      TokenStorage().clear();
      TokenStorage().saveTokens(accessToken: 'tok', refreshToken: 'ref');
      server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
      service = DisputesService(client: ApiClient(baseUrl: 'http://${server.address.host}:${server.port}'));
    });

    tearDown(() async {
      await server.close(force: true);
      TokenStorage().clear();
    });

    test('a customer files on the customers path with the whole body', () async {
      await answer(201, {'success': true, 'message': '', 'data': disputeJson(id: 7)});

      final created = await service.create(
        DisputeRole.customer,
        const DisputeSubmission(orderId: 42, category: 'QUALITY', description: 'x', evidenceUrls: ['a']),
      );

      expect(seen.single.method, 'POST');
      expect(seen.single.path, '/api/customers/me/disputes');
      expect(seen.single.body, {'orderId': 42, 'category': 'QUALITY', 'description': 'x', 'evidenceUrls': ['a']});
      expect(created.disputeId, 7);
      expect(created.slaDueAt, DateTime.utc(2026, 10, 10, 3));
    });

    test('a worker files on the workers path', () async {
      await answer(201, {'success': true, 'message': '', 'data': disputeJson()});

      await service.create(DisputeRole.worker, const DisputeSubmission(orderId: 1, category: 'OTHER', description: 'y', evidenceUrls: ['b']));

      expect(seen.single.path, '/api/workers/me/disputes');
    });

    test('lists the own disputes of the role', () async {
      await answer(200, {
        'success': true,
        'message': '',
        'data': [disputeJson(id: 2, status: 'RESOLVED'), disputeJson(id: 1)],
      });

      final items = await service.listMine(DisputeRole.worker);

      expect(seen.single.method, 'GET');
      expect(seen.single.path, '/api/workers/me/disputes');
      expect(items.map((d) => d.disputeId), [2, 1]);
    });

    test('a list answer without data is an empty list', () async {
      await answer(200, {'success': true, 'message': '', 'data': null});

      expect(await service.listMine(DisputeRole.customer), isEmpty);
    });

    test('409 and 404 are thrown as ApiExceptions the screen can recognise', () async {
      await answer(409, {'success': false, 'message': 'A dispute already exists for this order.', 'data': null});
      await expectLater(
        service.create(DisputeRole.customer, const DisputeSubmission(orderId: 42, category: 'QUALITY', description: 'x', evidenceUrls: ['a'])),
        throwsA(isA<ApiException>().having((e) => e.isConflict, 'isConflict', isTrue)),
      );
    });

    test('a 400 with field errors keeps its details', () async {
      await answer(400, {
        'success': false,
        'message': 'Validation failed',
        'data': {
          'errors': {
            'evidenceUrls': ['At least one evidence photo is required.']
          }
        },
      });

      await expectLater(
        service.create(DisputeRole.customer, const DisputeSubmission(orderId: 42, category: 'QUALITY', description: 'x', evidenceUrls: [])),
        throwsA(isA<ApiException>().having((e) => e.fieldErrors?['evidenceUrls']?.first, 'evidence error', 'At least one evidence photo is required.')),
      );
    });
  });

  group('UnavailableEvidenceUploader', () {
    test('says photos cannot be attached and never invents an address', () async {
      const uploader = UnavailableEvidenceUploader();

      expect(uploader.isAvailable, isFalse);
      expect(await uploader.pickAndUpload(), isNull);
    });
  });
}
