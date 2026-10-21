import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/auth/token_storage.dart';
import 'package:mobile/core/device/camera_wrapper.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/network/api_response.dart';
import 'package:mobile/core/widgets/nordic_text_input.dart';
import 'package:mobile/features/workers/models/job_photo_models.dart';
import 'package:mobile/features/workers/screens/worker_photos_screen.dart';
import 'package:mobile/features/workers/services/workers_photos_service.dart';

import 'design_frame.dart';

final jpeg = Uint8List.fromList([0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4]);

/// A camera and a library that hand out the photo the test chose (or fail like a refused permission).
class FakeCamera implements ICameraWrapper {
  CapturedImage? image;
  Object? error;
  int cameraCalls = 0;
  int libraryCalls = 0;

  FakeCamera(Uint8List? bytes)
    : image = bytes == null
          ? null
          : CapturedImage(
              path: '/cache/IMG_1.jpg',
              bytes: bytes,
              capturedAt: DateTime.utc(2026, 10, 21),
            );

  @override
  Future<CapturedImage?> capturePhoto() async {
    cameraCalls++;
    if (error != null) throw error!;
    return image;
  }

  @override
  Future<CapturedImage?> pickFromGallery() async {
    libraryCalls++;
    if (error != null) throw error!;
    return image;
  }
}

/// Records what was uploaded and answers like the server would, or fails with [failure].
class RecordingPhotosService extends WorkersPhotosService {
  final List<UploadJobPhotoRequest> uploads = [];
  ApiException? failure;

  RecordingPhotosService()
    : super(apiClient: ApiClient(baseUrl: 'http://api.test'));

  @override
  Future<ApiResponse<List<JobPhotoDto>>> getPhotos(int assignmentId) async =>
      ApiResponse<List<JobPhotoDto>>(success: true, message: 'OK', data: []);

  @override
  Future<ApiResponse<JobPhotoDto>> uploadPhoto({
    required int assignmentId,
    required UploadJobPhotoRequest request,
  }) async {
    uploads.add(request);
    if (failure != null) throw failure!;
    return ApiResponse<JobPhotoDto>(
      success: true,
      message: 'OK',
      data: JobPhotoDto(
        photoId: 9000 + uploads.length,
        assignmentId: assignmentId,
        photoPhase: request.photoPhase,
        angleNo: request.angleNo,
        photoUrl:
            '/api/workers/assignments/$assignmentId/photos/${9000 + uploads.length}/file',
        volScore: 142.5,
        isAccepted: true,
        uploadedAt: DateTime(2026, 10, 21, 9),
      ),
    );
  }
}

Widget screen(RecordingPhotosService service, FakeCamera camera) => MaterialApp(
  home: WorkerPhotosScreen(
    assignmentId: 2001,
    photosService: service,
    camera: camera,
  ),
);

void main() {
  setUp(() => TokenStorage().clear());

  group('WorkerPhotosScreen Tests', () {
    testWidgets(
      'Renders header, phase selector, angle selector, camera guide, and the photo buttons',
      (tester) async {
        useDesignFrame(tester);
        await tester.pumpWidget(
          screen(RecordingPhotosService(), FakeCamera(jpeg)),
        );
        await tester.pumpAndSettle();

        expect(find.text('Ảnh Before/After #2001'), findsOneWidget);
        expect(find.text('KIỂM TRA CHẤT LƯỢNG VOL'), findsOneWidget);
        expect(find.text('BEFORE (Trước)'), findsOneWidget);
        expect(find.text('AFTER (Sau)'), findsOneWidget);
        expect(find.text('Góc 1'), findsOneWidget);
        expect(find.text('Góc 1: Toàn cảnh'), findsOneWidget);
        expect(find.text('Chụp ảnh'), findsOneWidget);
        expect(find.text('Chọn từ thư viện'), findsOneWidget);
        expect(find.text('Gửi Ảnh Chụp'), findsOneWidget);
      },
    );

    testWidgets(
      'There is no URL box and no fake blurry-photo button any more',
      (tester) async {
        useDesignFrame(tester);
        await tester.pumpWidget(
          screen(RecordingPhotosService(), FakeCamera(jpeg)),
        );
        await tester.pumpAndSettle();

        expect(find.byType(NordicTextInput), findsNothing);
        expect(find.byType(TextField), findsNothing);
        expect(find.textContaining('Tải Ảnh Mờ'), findsNothing);
        expect(find.textContaining('storage.giupviec.local'), findsNothing);
      },
    );

    testWidgets('Switching angles updates camera guide text', (tester) async {
      useDesignFrame(tester);
      await tester.pumpWidget(
        screen(RecordingPhotosService(), FakeCamera(jpeg)),
      );
      await tester.pumpAndSettle();

      expect(find.text('Góc 1: Toàn cảnh'), findsOneWidget);

      await tapVisible(tester, find.text('Góc 2'));
      await tester.pumpAndSettle();

      expect(find.text('Góc 2: Bàn ghế & tủ'), findsOneWidget);
    });

    testWidgets('Sending without a photo asks for one and uploads nothing', (
      tester,
    ) async {
      useDesignFrame(tester);
      final service = RecordingPhotosService();
      await tester.pumpWidget(screen(service, FakeCamera(jpeg)));
      await tester.pumpAndSettle();

      await tapVisible(tester, find.text('Gửi Ảnh Chụp'));
      await scrollTo(tester, find.textContaining('chụp hoặc chọn ảnh'));

      expect(find.textContaining('chụp hoặc chọn ảnh'), findsOneWidget);
      expect(service.uploads, isEmpty);
    });

    testWidgets(
      'A photo from the camera is uploaded as a file with the chosen phase and angle',
      (tester) async {
        useDesignFrame(tester);
        final service = RecordingPhotosService();
        final camera = FakeCamera(jpeg);
        await tester.pumpWidget(screen(service, camera));
        await tester.pumpAndSettle();

        await tapVisible(tester, find.text('Góc 3'));
        await tapVisible(tester, find.text('Chụp ảnh'));
        await tapVisible(tester, find.text('Gửi Ảnh Chụp'));
        await scrollTo(tester, find.textContaining('Tải ảnh thành công'));

        expect(camera.cameraCalls, 1);
        expect(service.uploads, hasLength(1));
        final upload = service.uploads.single;
        expect(upload.photoPhase, 'BEFORE');
        expect(upload.angleNo, 3);
        expect(upload.bytes, jpeg);
        expect(upload.contentType, 'image/jpeg');
        expect(find.textContaining('Tải ảnh thành công'), findsOneWidget);
        expect(find.textContaining('142.5'), findsWidgets);
        expect(find.textContaining('ĐẠT CHUẨN'), findsOneWidget);
      },
    );

    testWidgets('A photo from the library is uploaded the same way', (
      tester,
    ) async {
      useDesignFrame(tester);
      final service = RecordingPhotosService();
      final camera = FakeCamera(jpeg);
      await tester.pumpWidget(screen(service, camera));
      await tester.pumpAndSettle();

      await tapVisible(tester, find.text('Chọn từ thư viện'));
      await tapVisible(tester, find.text('Gửi Ảnh Chụp'));

      expect(camera.libraryCalls, 1);
      expect(service.uploads, hasLength(1));
    });

    testWidgets(
      'A blurry photo shows the server message and is cleared so the worker retakes it',
      (tester) async {
        useDesignFrame(tester);
        final service = RecordingPhotosService()
          ..failure = const ApiException(
            400,
            'Validation failed',
            fieldErrors: {
              'volScore': [
                'Ảnh bị mờ (độ nét 45.2, cần tối thiểu 100.0). Vui lòng chụp lại ảnh rõ hơn.',
              ],
            },
          );
        await tester.pumpWidget(screen(service, FakeCamera(jpeg)));
        await tester.pumpAndSettle();

        await tapVisible(tester, find.text('Chụp ảnh'));
        await tapVisible(tester, find.text('Gửi Ảnh Chụp'));
        await scrollTo(tester, find.textContaining('Ảnh bị mờ'));

        expect(find.textContaining('Ảnh bị mờ (độ nét 45.2'), findsOneWidget);
        expect(find.textContaining('Tải ảnh thành công'), findsNothing);
        expect(find.textContaining('0/5 đạt'), findsOneWidget);

        // the blurry photo is gone: sending again needs a new one
        await tapVisible(tester, find.text('Gửi Ảnh Chụp'));
        expect(service.uploads, hasLength(1));
      },
    );

    testWidgets(
      'A refused file type and a too large file are shown in Vietnamese by their field',
      (tester) async {
        useDesignFrame(tester);
        final service = RecordingPhotosService()
          ..failure = const ApiException(
            400,
            'Validation failed',
            fieldErrors: {
              'contentType': ['unsupported'],
            },
          );
        await tester.pumpWidget(screen(service, FakeCamera(jpeg)));
        await tester.pumpAndSettle();
        await tapVisible(tester, find.text('Chụp ảnh'));
        await tapVisible(tester, find.text('Gửi Ảnh Chụp'));
        await scrollTo(tester, find.textContaining('JPEG, PNG hoặc WebP'));
        expect(
          find.textContaining('Chỉ nhận ảnh JPEG, PNG hoặc WebP'),
          findsOneWidget,
        );

        service.failure = const ApiException(
          400,
          'Validation failed',
          fieldErrors: {
            'file': ['The file is too large'],
          },
        );
        await tapVisible(tester, find.text('Gửi Ảnh Chụp'));
        await scrollTo(tester, find.textContaining('lớn hơn 5 MB'));
        expect(find.textContaining('lớn hơn 5 MB'), findsOneWidget);
      },
    );

    testWidgets('A network failure is an error, not a made-up accepted photo', (
      tester,
    ) async {
      useDesignFrame(tester);
      final service = RecordingPhotosService()
        ..failure = const ApiException(
          0,
          'Không kết nối được máy chủ. Vui lòng kiểm tra mạng và thử lại.',
        );
      await tester.pumpWidget(screen(service, FakeCamera(jpeg)));
      await tester.pumpAndSettle();

      await tapVisible(tester, find.text('Chụp ảnh'));
      await tapVisible(tester, find.text('Gửi Ảnh Chụp'));
      await scrollTo(tester, find.textContaining('Không kết nối được máy chủ'));

      expect(find.textContaining('Không kết nối được máy chủ'), findsOneWidget);
      expect(find.textContaining('Tải ảnh thành công'), findsNothing);
      expect(find.textContaining('Đã tải ảnh lên'), findsNothing);
      expect(find.textContaining('0/5 đạt'), findsOneWidget);
    });

    testWidgets(
      'A file that is not JPEG, PNG or WebP is refused before it is sent',
      (tester) async {
        useDesignFrame(tester);
        final service = RecordingPhotosService();
        await tester.pumpWidget(
          screen(
            service,
            FakeCamera(Uint8List.fromList('not an image'.codeUnits)),
          ),
        );
        await tester.pumpAndSettle();

        await tapVisible(tester, find.text('Chọn từ thư viện'));
        await scrollTo(tester, find.textContaining('JPEG, PNG hoặc WebP'));
        expect(
          find.textContaining('Chỉ nhận ảnh JPEG, PNG hoặc WebP'),
          findsOneWidget,
        );

        await tapVisible(tester, find.text('Gửi Ảnh Chụp'));
        expect(service.uploads, isEmpty);
      },
    );

    testWidgets('A photo of more than 5 MB is refused before it is sent', (
      tester,
    ) async {
      useDesignFrame(tester);
      final service = RecordingPhotosService();
      final big = Uint8List(UploadJobPhotoRequest.maxBytes + 1)
        ..setRange(0, 3, [0xFF, 0xD8, 0xFF]);
      await tester.pumpWidget(screen(service, FakeCamera(big)));
      await tester.pumpAndSettle();

      await tapVisible(tester, find.text('Chụp ảnh'));
      await scrollTo(tester, find.textContaining('lớn hơn 5 MB'));

      expect(find.textContaining('Ảnh lớn hơn 5 MB'), findsOneWidget);
      await tapVisible(tester, find.text('Gửi Ảnh Chụp'));
      expect(service.uploads, isEmpty);
    });

    testWidgets(
      'A refused camera permission is explained and nothing is uploaded',
      (tester) async {
        useDesignFrame(tester);
        final service = RecordingPhotosService();
        final camera = FakeCamera(jpeg)
          ..error = StateError('camera_access_denied');
        await tester.pumpWidget(screen(service, camera));
        await tester.pumpAndSettle();

        await tapVisible(tester, find.text('Chụp ảnh'));
        await scrollTo(tester, find.textContaining('Không mở được camera'));

        expect(
          find.textContaining('Không mở được camera hoặc thư viện ảnh'),
          findsOneWidget,
        );
        expect(service.uploads, isEmpty);
      },
    );

    testWidgets('Cancelling the camera changes nothing', (tester) async {
      useDesignFrame(tester);
      final service = RecordingPhotosService();
      await tester.pumpWidget(screen(service, FakeCamera(null)));
      await tester.pumpAndSettle();

      await tapVisible(tester, find.text('Chụp ảnh'));
      await tapVisible(tester, find.text('Gửi Ảnh Chụp'));

      expect(service.uploads, isEmpty);
    });
  });
}
