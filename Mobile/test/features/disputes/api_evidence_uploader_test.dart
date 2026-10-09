import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/auth/token_storage.dart';
import 'package:mobile/core/device/camera_wrapper.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/disputes/models/dispute_models.dart';
import 'package:mobile/features/disputes/services/api_evidence_uploader.dart';

class FakeCamera implements ICameraWrapper {
  CapturedImage? image;
  int captures = 0;

  FakeCamera(this.image);

  @override
  Future<CapturedImage?> capturePhoto() async {
    captures++;
    return image;
  }

  @override
  Future<CapturedImage?> pickFromGallery() async => image;
}

final png = Uint8List.fromList([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]);
final jpeg = Uint8List.fromList([0xFF, 0xD8, 0xFF, 0xE0, 9, 9]);
final webp = Uint8List.fromList([0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50, 1]);

CapturedImage photo(Uint8List? bytes, {String path = '/data/user/0/app/cache/IMG_001.png'}) =>
    CapturedImage(path: path, bytes: bytes, capturedAt: DateTime.utc(2026, 10, 9));

void main() {
  group('imageContentType', () {
    test('recognises JPEG, PNG and WebP from the first bytes and nothing else', () {
      expect(imageContentType(jpeg), 'image/jpeg');
      expect(imageContentType(png), 'image/png');
      expect(imageContentType(webp), 'image/webp');
      expect(imageContentType(Uint8List.fromList(utf8.encode('<html></html>'))), isNull);
      expect(imageContentType(Uint8List(0)), isNull);
      expect(imageContentType(Uint8List.fromList([0x89, 0x50])), isNull);
    });
  });

  group('fileNameOf', () {
    test('keeps the last segment of a device path and falls back to photo.<ext>', () {
      expect(fileNameOf('/data/app/IMG_001.png', 'image/png'), 'IMG_001.png');
      expect(fileNameOf(r'C:\photos\a b.jpg', 'image/jpeg'), 'a b.jpg');
      expect(fileNameOf('', 'image/webp'), 'photo.webp');
      expect(fileNameOf('/', 'image/jpeg'), 'photo.jpg');
      expect(fileNameOf('/x/${'a' * 150}.png', 'image/png').length, 100);
    });
  });

  group('ApiEvidenceUploader over a local server', () {
    late HttpServer server;
    final seen = <({String method, String path, Map<String, dynamic>? body})>[];
    var status = 201;
    Object? answerBody;

    ApiEvidenceUploader uploader(CapturedImage? image, {DisputeRole role = DisputeRole.customer}) => ApiEvidenceUploader(
          role: role,
          camera: FakeCamera(image),
          client: ApiClient(baseUrl: 'http://${server.address.host}:${server.port}'),
        );

    setUp(() async {
      seen.clear();
      status = 201;
      answerBody = {
        'success': true,
        'message': '',
        'data': {'path': 'dispute-evidence/x.png', 'url': '/files/dispute-evidence/x.png', 'sizeBytes': 11},
      };
      TokenStorage().clear();
      TokenStorage().saveTokens(accessToken: 'tok', refreshToken: 'ref');
      server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
      server.listen((HttpRequest request) async {
        final raw = await utf8.decodeStream(request);
        seen.add((method: request.method, path: request.uri.path, body: raw.isEmpty ? null : jsonDecode(raw) as Map<String, dynamic>));
        request.response.statusCode = status;
        request.response.headers.contentType = ContentType.json;
        request.response.write(jsonEncode(answerBody));
        await request.response.close();
      });
    });

    tearDown(() async {
      await server.close(force: true);
      TokenStorage().clear();
    });

    test('is available', () {
      expect(uploader(null).isAvailable, isTrue);
    });

    test('a customer photo is sent as JSON with its detected type and base64, and the url comes back', () async {
      final url = await uploader(photo(png)).pickAndUpload();

      expect(url, '/files/dispute-evidence/x.png');
      expect(seen.single.method, 'POST');
      expect(seen.single.path, '/api/customers/me/disputes/evidence');
      expect(seen.single.body, {
        'fileName': 'IMG_001.png',
        'contentType': 'image/png',
        'contentBase64': base64Encode(png),
      });
    });

    test('a worker photo goes to the workers path; a jpeg is named after its type when the device path has no name', () async {
      await uploader(photo(jpeg, path: ''), role: DisputeRole.worker).pickAndUpload();

      expect(seen.single.path, '/api/workers/me/disputes/evidence');
      expect(seen.single.body!['contentType'], 'image/jpeg');
      expect(seen.single.body!['fileName'], 'photo.jpg');
    });

    test('a cancelled capture returns null and sends nothing', () async {
      expect(await uploader(null).pickAndUpload(), isNull);
      expect(seen, isEmpty);
    });

    test('a photo without bytes or in another format is an ApiException before any request', () async {
      await expectLater(uploader(photo(null)).pickAndUpload(), throwsA(isA<ApiException>().having((e) => e.message, 'message', contains('Không đọc được ảnh'))));
      await expectLater(uploader(photo(Uint8List(0))).pickAndUpload(), throwsA(isA<ApiException>()));
      await expectLater(
        uploader(photo(Uint8List.fromList(utf8.encode('<script>')))).pickAndUpload(),
        throwsA(isA<ApiException>().having((e) => e.message, 'message', contains('JPEG, PNG hoặc WebP'))),
      );
      expect(seen, isEmpty);
    });

    test('a 400 of the server is thrown with its message', () async {
      status = 400;
      answerBody = {'success': false, 'message': 'Validation failed', 'data': null};

      await expectLater(uploader(photo(png)).pickAndUpload(), throwsA(isA<ApiException>().having((e) => e.statusCode, 'status', 400)));
    });

    test('an answer without a url is an error, not a silent null', () async {
      answerBody = {'success': true, 'message': '', 'data': {'path': 'x', 'sizeBytes': 1}};

      await expectLater(uploader(photo(png)).pickAndUpload(), throwsA(isA<ApiException>()));
    });
  });
}
