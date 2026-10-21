import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/auth/token_storage.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/workers/models/job_photo_models.dart';
import 'package:mobile/features/workers/services/workers_photos_service.dart';

/// One part of a received `multipart/form-data` body.
class Part {
  final String name;
  final String? fileName;
  final String? contentType;
  final List<int> bytes;

  Part(this.name, this.fileName, this.contentType, this.bytes);
}

/// Reads the body byte for byte (latin1 keeps every byte as one character), so the file bytes can be compared exactly.
List<Part> parseMultipart(String contentType, List<int> body) {
  final boundary = RegExp(r'boundary=(.+)$').firstMatch(contentType)!.group(1)!;
  final text = latin1.decode(body);
  final chunks = text.split('--$boundary');
  expect(chunks.first, isEmpty, reason: 'the body starts with the boundary');
  expect(chunks.last.trim(), '--', reason: 'the body ends with --boundary--');

  final parts = <Part>[];
  for (final chunk in chunks.sublist(1, chunks.length - 1)) {
    final split = chunk.indexOf('\r\n\r\n');
    final head = chunk.substring(
      2,
      split,
    ); // after the CRLF that follows the boundary
    var content = chunk.substring(split + 4);
    expect(content.endsWith('\r\n'), isTrue);
    content = content.substring(0, content.length - 2);
    final disposition = RegExp(
      r'Content-Disposition: form-data; name="([^"]*)"(?:; filename="([^"]*)")?',
    ).firstMatch(head)!;
    parts.add(
      Part(
        disposition.group(1)!,
        disposition.group(2),
        RegExp(r'Content-Type: (.+)').firstMatch(head)?.group(1),
        latin1.encode(content),
      ),
    );
  }
  return parts;
}

final jpeg = Uint8List.fromList([
  0xFF, 0xD8, 0xFF, 0xE0, //
  0x0D, 0x0A, 0x2D, 0x2D, 0x00, 0xFF, 0x80, // a CRLF and dashes inside the data
  0xFF, 0xD9,
]);

void main() {
  group('WorkersPhotosService.uploadPhoto over a local server', () {
    late HttpServer server;
    late WorkersPhotosService service;
    final received =
        <
          ({
            String path,
            String? contentType,
            String? auth,
            int? length,
            List<int> body,
          })
        >[];
    var statuses = <int>[];
    late Object answerJson;

    Future<void> serve() async {
      server.listen((HttpRequest request) async {
        final body = await request.fold<List<int>>([], (a, b) => a..addAll(b));
        if (request.uri.path == '/api/auth/refresh') {
          request.response.headers.contentType = ContentType.json;
          request.response.write(
            jsonEncode({
              'success': true,
              'data': {'accessToken': 'fresh', 'refreshToken': 'ref2'},
            }),
          );
          await request.response.close();
          return;
        }
        received.add((
          path: request.uri.path,
          contentType: request.headers.contentType?.toString(),
          auth: request.headers.value('authorization'),
          length: request.contentLength,
          body: body,
        ));
        request.response.statusCode = statuses.isEmpty
            ? 200
            : statuses.removeAt(0);
        request.response.headers.contentType = ContentType.json;
        request.response.write(jsonEncode(answerJson));
        await request.response.close();
      });
    }

    setUp(() async {
      received.clear();
      statuses = [];
      TokenStorage().clear();
      TokenStorage().saveTokens(accessToken: 'tok', refreshToken: 'ref');
      server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
      service = WorkersPhotosService(
        apiClient: ApiClient(
          baseUrl: 'http://${server.address.host}:${server.port}',
        ),
      );
      answerJson = {
        'success': true,
        'message': 'OK',
        'data': {
          'photoId': 801,
          'assignmentId': 2001,
          'photoPhase': 'BEFORE',
          'angleNo': 2,
          'photoUrl': '/api/workers/assignments/2001/photos/801/file',
          'volScore': 142.5,
          'isAccepted': true,
          'uploadedAt': '2026-10-21T08:00:00Z',
        },
      };
      await serve();
    });

    tearDown(() async {
      await server.close(force: true);
      TokenStorage().clear();
    });

    UploadJobPhotoRequest request() => UploadJobPhotoRequest(
      photoPhase: 'BEFORE',
      angleNo: 2,
      bytes: jpeg,
      contentType: 'image/jpeg',
    );

    test('sends the file in "file" with photoPhase and angleNo as multipart/form-data', () async {
      final response = await service.uploadPhoto(
        assignmentId: 2001,
        request: request(),
      );

      expect(received, hasLength(1));
      final call = received.single;
      expect(call.path, '/api/workers/assignments/2001/photos');
      expect(call.contentType, startsWith('multipart/form-data; boundary='));
      expect(call.auth, 'Bearer tok');
      expect(
        call.length,
        call.body.length,
        reason: 'a Content-Length, not chunked',
      );

      final parts = parseMultipart(call.contentType!, call.body);
      expect(parts.map((p) => p.name), ['photoPhase', 'angleNo', 'file']);
      expect(utf8.decode(parts[0].bytes), 'BEFORE');
      expect(utf8.decode(parts[1].bytes), '2');
      final file = parts[2];
      expect(file.fileName, 'before_angle2.jpg');
      expect(file.contentType, 'image/jpeg');
      expect(
        file.bytes,
        jpeg,
        reason:
            'the file arrives byte for byte, even with CRLF and dashes inside',
      );

      expect(response.data!.photoId, 801);
      expect(response.data!.volScore, 142.5);
      expect(response.data!.isAccepted, isTrue);
      expect(
        response.data!.photoUrl,
        '/api/workers/assignments/2001/photos/801/file',
      );
    });

    test('names a PNG and a WebP by their type, never by the device path', () {
      expect(
        UploadJobPhotoRequest(
          photoPhase: 'AFTER',
          angleNo: 3,
          bytes: jpeg,
          contentType: 'image/png',
        ).fileName,
        'after_angle3.png',
      );
      expect(
        UploadJobPhotoRequest(
          photoPhase: 'AFTER',
          angleNo: 1,
          bytes: jpeg,
          contentType: 'image/webp',
        ).fileName,
        'after_angle1.webp',
      );
    });

    test(
      'a blurry photo is a 400 whose volScore message reaches the caller',
      () async {
        statuses = [400];
        answerJson = {
          'success': false,
          'message': 'Validation failed',
          'data': {
            'errors': {
              'volScore': [
                'Ảnh bị mờ (độ nét 45.2, cần tối thiểu 100.0). Vui lòng chụp lại ảnh rõ hơn.',
              ],
            },
          },
        };

        try {
          await service.uploadPhoto(assignmentId: 2001, request: request());
          fail('the upload should have thrown');
        } on ApiException catch (e) {
          expect(e.statusCode, 400);
          expect(e.fieldErrors!['volScore']!.single, contains('Ảnh bị mờ'));
        }
      },
    );

    test(
      'after a 401 the token is refreshed and the same file is sent again',
      () async {
        statuses = [401, 200];

        final response = await service.uploadPhoto(
          assignmentId: 2001,
          request: request(),
        );

        expect(received, hasLength(2));
        expect(received[0].auth, 'Bearer tok');
        expect(received[1].auth, 'Bearer fresh');
        final again = parseMultipart(
          received[1].contentType!,
          received[1].body,
        );
        expect(again.last.bytes, jpeg);
        expect(response.data!.photoId, 801);
      },
    );
  });

  group('WorkersPhotosService.photoImage', () {
    late WorkersPhotosService service;

    setUp(() {
      TokenStorage().clear();
      TokenStorage().saveTokens(accessToken: 'tok', refreshToken: 'ref');
      service = WorkersPhotosService(
        apiClient: ApiClient(baseUrl: 'http://api.example:5004'),
      );
    });

    tearDown(() => TokenStorage().clear());

    test('a stored photo is loaded from the API address with the Bearer token', () {
      final image = service.photoImage(
        '/api/workers/assignments/2001/photos/801/file',
      );

      expect(
        image!.url,
        'http://api.example:5004/api/workers/assignments/2001/photos/801/file',
      );
      expect(image.headers, {'Authorization': 'Bearer tok'});
    });

    test(
      'a client URL is never given the token (it would go to another host)',
      () {
        expect(
          service.photoImage('https://storage.giupviec.local/jobs/a.jpg'),
          isNull,
        );
        expect(service.photoImage(''), isNull);
      },
    );
  });
}
