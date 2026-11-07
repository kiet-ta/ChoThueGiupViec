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
import 'fake_assignments_service.dart';

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
  final List<JobPhotoDto> existingPhotos = [];
  ApiException? failure;
  int submitCompletionCalls = 0;
  ApiException? submitFailure;

  RecordingPhotosService({List<JobPhotoDto>? initialPhotos})
    : super(apiClient: ApiClient(baseUrl: 'http://api.test')) {
    if (initialPhotos != null) {
      existingPhotos.addAll(initialPhotos);
    }
  }

  @override
  Future<ApiResponse<List<JobPhotoDto>>> getPhotos(
    int assignmentId,
  ) async => ApiResponse<List<JobPhotoDto>>(
    success: true,
    message: 'OK',
    data: [
      ...existingPhotos,
      ...uploads.asMap().entries.map(
        (e) => JobPhotoDto(
          photoId: 9000 + e.key,
          assignmentId: assignmentId,
          photoPhase: e.value.photoPhase,
          angleNo: e.value.angleNo,
          photoUrl:
              '/api/v1/workers/assignments/$assignmentId/photos/${9000 + e.key}/file',
          volScore: 142.5,
          isAccepted: true,
          uploadedAt: DateTime(2026, 10, 21, 9),
        ),
      ),
    ],
  );

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
            '/api/v1/workers/assignments/$assignmentId/photos/${9000 + uploads.length}/file',
        volScore: 142.5,
        isAccepted: true,
        uploadedAt: DateTime(2026, 10, 21, 9),
      ),
    );
  }

  @override
  Future<ApiResponse<void>> submitCompletion(
    int assignmentId, {
    String? notes,
  }) async {
    submitCompletionCalls++;
    if (submitFailure != null) throw submitFailure!;
    return ApiResponse<void>(success: true, message: 'Hoàn tất nghiệm thu');
  }
}

Widget screen(
  RecordingPhotosService service,
  FakeCamera camera, {
  FakeAssignmentsService? assignments,
}) => MaterialApp(
  home: WorkerPhotosScreen(
    assignmentId: 2001,
    photosService: service,
    assignmentsService: assignments ?? FakeAssignmentsService(),
    camera: camera,
  ),
);

List<JobPhotoDto> allFivePhotos() => [
  for (var i = 1; i <= 5; i++)
    for (final phase in ['BEFORE', 'AFTER'])
      JobPhotoDto(
        photoId: (phase == 'BEFORE' ? 100 : 200) + i,
        assignmentId: 2001,
        photoPhase: phase,
        angleNo: i,
        photoUrl: '/photos/$i/$phase',
        volScore: 120.0,
        isAccepted: true,
        uploadedAt: DateTime.now(),
      ),
];

void main() {
  setUp(() => TokenStorage().clear());

  group('WorkerPhotosScreen Tests', () {
    testWidgets(
      'Renders header, stats, hero card, angle checklist, and immediate action buttons',
      (tester) async {
        useDesignFrame(tester);
        await tester.pumpWidget(
          screen(RecordingPhotosService(), FakeCamera(jpeg)),
        );
        await tester.pumpAndSettle();

        expect(find.text('Ảnh trước và sau'), findsOneWidget);
        expect(find.text('Trước'), findsWidgets);
        expect(find.text('Sau'), findsWidgets);
        expect(find.text('Góc 1: Toàn cảnh'), findsOneWidget);
        expect(find.text('Chụp ảnh'), findsOneWidget);
        expect(find.text('Chọn từ thư viện'), findsOneWidget);
        expect(find.text('Thêm góc'), findsOneWidget);
        expect(find.text('Toàn cảnh'), findsOneWidget);
        expect(find.text('Bàn ghế & tủ'), findsOneWidget);
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

    testWidgets('Switching angles in checklist updates hero guide card', (
      tester,
    ) async {
      useDesignFrame(tester);
      await tester.pumpWidget(
        screen(RecordingPhotosService(), FakeCamera(jpeg)),
      );
      await tester.pumpAndSettle();

      expect(find.text('Góc 1: Toàn cảnh'), findsOneWidget);

      await tapVisible(tester, find.byKey(const Key('photo-angle-2')));
      await tester.pumpAndSettle();

      expect(find.text('Góc 2: Bàn ghế & tủ'), findsOneWidget);
    });

    testWidgets('Tapping mini pill of an angle selects that phase and angle', (
      tester,
    ) async {
      useDesignFrame(tester);
      await tester.pumpWidget(
        screen(RecordingPhotosService(), FakeCamera(jpeg)),
      );
      await tester.pumpAndSettle();

      await tapVisible(tester, find.byKey(const Key('photo-cell-AFTER-4')));
      await tester.pumpAndSettle();

      expect(find.text('Góc 4: Sàn nhà'), findsOneWidget);
    });

    testWidgets(
      'A photo from the camera is immediately captured, uploaded and verified',
      (tester) async {
        useDesignFrame(tester);
        final service = RecordingPhotosService();
        final camera = FakeCamera(jpeg);
        await tester.pumpWidget(screen(service, camera));
        await tester.pumpAndSettle();

        await tapVisible(tester, find.byKey(const Key('photo-angle-3')));
        await tester.pumpAndSettle();

        // Tapping 'Chụp ảnh' immediately captures and uploads
        await tapVisible(tester, find.text('Chụp ảnh'));
        await tester.pumpAndSettle();

        expect(camera.cameraCalls, 1);
        expect(service.uploads, hasLength(1));
        final upload = service.uploads.single;
        expect(upload.photoPhase, 'BEFORE');
        expect(upload.angleNo, 3);
        expect(upload.bytes, jpeg);
        expect(upload.contentType, 'image/jpeg');

        // Shows uploaded and auto-advances to AFTER for angle 3
        expect(find.textContaining('Đã tải ảnh lên'), findsOneWidget);
        expect(find.textContaining('142.5'), findsWidgets);
      },
    );

    testWidgets(
      'A photo from the library is uploaded immediately the same way',
      (tester) async {
        useDesignFrame(tester);
        final service = RecordingPhotosService();
        final camera = FakeCamera(jpeg);
        await tester.pumpWidget(screen(service, camera));
        await tester.pumpAndSettle();

        await tapVisible(tester, find.text('Chọn từ thư viện'));
        await tester.pumpAndSettle();

        expect(camera.libraryCalls, 1);
        expect(service.uploads, hasLength(1));
      },
    );

    testWidgets(
      'A blurry photo shows the server message and does not advance',
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
        await tester.pumpAndSettle();

        expect(find.textContaining('Ảnh bị mờ (độ nét 45.2'), findsOneWidget);
        expect(find.textContaining('Đã tải ảnh lên'), findsNothing);
      },
    );

    testWidgets(
      'A refused file type and a too large file are rejected locally with messages',
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
        await tester.pumpAndSettle();

        expect(
          find.textContaining('Chỉ nhận ảnh JPEG, PNG hoặc WebP'),
          findsOneWidget,
        );
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
      await tester.pumpAndSettle();

      expect(find.textContaining('Ảnh lớn hơn 5 MB'), findsOneWidget);
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
        await tester.pumpAndSettle();

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
      await tester.pumpAndSettle();

      expect(service.uploads, isEmpty);
    });

    testWidgets(
      'Option A: minimum 5 angles cannot be deleted, extra angle can be added and deleted',
      (tester) async {
        useDesignFrame(tester);
        final service = RecordingPhotosService();
        await tester.pumpWidget(screen(service, FakeCamera(jpeg)));
        await tester.pumpAndSettle();

        // Initial 5 angles cannot be deleted (Option A)
        expect(find.byKey(const Key('delete-angle-1')), findsNothing);
        expect(find.byKey(const Key('delete-angle-5')), findsNothing);

        // Tap 'Thêm góc'
        await tapVisible(tester, find.text('Thêm góc'));
        await tester.pumpAndSettle();

        expect(find.text('Thêm góc chụp'), findsWidgets);

        // Enter angle title
        await tester.enterText(
          find.byKey(const Key('add-angle-name-input')),
          'Khu vực ban công',
        );
        await tester.pumpAndSettle();

        // Tap 'Thêm'
        await tapVisible(tester, find.text('Thêm'));
        await tester.pumpAndSettle();

        // Now angle 6 exists and can be deleted
        expect(find.text('Khu vực ban công'), findsOneWidget);
        expect(find.byKey(const Key('delete-angle-6')), findsOneWidget);

        // Delete angle 6
        await tapVisible(tester, find.byKey(const Key('delete-angle-6')));
        await tester.pumpAndSettle();

        // Angle 6 is removed, back to 5 angles
        expect(find.text('Khu vực ban công'), findsNothing);
        expect(find.byKey(const Key('delete-angle-6')), findsNothing);
      },
    );

    testWidgets('Transition to "Nghiệm thu" when all angles are completed', (
      tester,
    ) async {
      useDesignFrame(tester);
      // Prepare 5 angles with BEFORE and AFTER photos
      final fullPhotos = <JobPhotoDto>[];
      for (int i = 1; i <= 5; i++) {
        fullPhotos.add(
          JobPhotoDto(
            photoId: 100 + i,
            assignmentId: 2001,
            photoPhase: 'BEFORE',
            angleNo: i,
            photoUrl: '/photos/$i/before',
            volScore: 120.0,
            isAccepted: true,
            uploadedAt: DateTime.now(),
          ),
        );
        fullPhotos.add(
          JobPhotoDto(
            photoId: 200 + i,
            assignmentId: 2001,
            photoPhase: 'AFTER',
            angleNo: i,
            photoUrl: '/photos/$i/after',
            volScore: 130.0,
            isAccepted: true,
            uploadedAt: DateTime.now(),
          ),
        );
      }

      final service = RecordingPhotosService(initialPhotos: fullPhotos);
      await tester.pumpWidget(screen(service, FakeCamera(jpeg)));
      await tester.pumpAndSettle();

      // Primary action button is now 'Nghiệm thu'
      expect(find.text('Nghiệm thu'), findsOneWidget);

      // Tap 'Nghiệm thu'
      await tapVisible(tester, find.text('Nghiệm thu'));
      await tester.pumpAndSettle();

      expect(service.submitCompletionCalls, 1);
    });

    testWidgets(
      'A job already awaiting acceptance has no Nghiệm thu / Chụp ảnh button and says it is waiting (#677)',
      (tester) async {
        useDesignFrame(tester);
        final service = RecordingPhotosService(initialPhotos: allFivePhotos());
        await tester.pumpWidget(
          screen(
            service,
            FakeCamera(jpeg),
            assignments: FakeAssignmentsService(status: 'AWAITING_ACCEPTANCE'),
          ),
        );
        await tester.pumpAndSettle();

        expect(find.byKey(const Key('photo-submit-button')), findsNothing);
        expect(find.byKey(const Key('photo-capture-button')), findsNothing);
        expect(find.text('Nghiệm thu'), findsNothing);
        expect(
          find.textContaining('Đang chờ gia chủ xác nhận'),
          findsOneWidget,
        );
        expect(service.submitCompletionCalls, 0);
      },
    );

    testWidgets('A completed job also hides the submit action (#677)', (
      tester,
    ) async {
      useDesignFrame(tester);
      await tester.pumpWidget(
        screen(
          RecordingPhotosService(initialPhotos: allFivePhotos()),
          FakeCamera(jpeg),
          assignments: FakeAssignmentsService(status: 'COMPLETED'),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('photo-submit-button')), findsNothing);
    });

    testWidgets('An IN_PROGRESS job with 5/5 photos still shows Nghiệm thu', (
      tester,
    ) async {
      useDesignFrame(tester);
      await tester.pumpWidget(
        screen(
          RecordingPhotosService(initialPhotos: allFivePhotos()),
          FakeCamera(jpeg),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('photo-submit-button')), findsOneWidget);
      expect(find.textContaining('Đang chờ gia chủ xác nhận'), findsNothing);
    });

    testWidgets('When the status cannot be read the worker can still submit', (
      tester,
    ) async {
      useDesignFrame(tester);
      final service = RecordingPhotosService(initialPhotos: allFivePhotos());
      await tester.pumpWidget(
        screen(
          service,
          FakeCamera(jpeg),
          assignments: FakeAssignmentsService(
            failure: const ApiException(0, 'offline'),
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('photo-submit-button')), findsOneWidget);
      await tapVisible(tester, find.text('Nghiệm thu'));
      expect(service.submitCompletionCalls, 1);
    });

    testWidgets(
      'If the server says the job is already submitted, the screen locks with a Vietnamese notice, not the English error',
      (tester) async {
        useDesignFrame(tester);
        final assignments = FakeAssignmentsService();
        final service = RecordingPhotosService(initialPhotos: allFivePhotos())
          ..submitFailure = const ApiException(
            409,
            'Cannot submit completion for assignment in status AwaitingAcceptance.',
          );
        await tester.pumpWidget(
          screen(service, FakeCamera(jpeg), assignments: assignments),
        );
        await tester.pumpAndSettle();

        // Another device submits meanwhile.
        assignments.status = 'AWAITING_ACCEPTANCE';
        await tapVisible(tester, find.text('Nghiệm thu'));
        await tester.pumpAndSettle();

        expect(service.submitCompletionCalls, 1);
        expect(find.textContaining('Cannot submit'), findsNothing);
        expect(
          find.textContaining('Đang chờ gia chủ xác nhận'),
          findsOneWidget,
        );
        expect(find.byKey(const Key('photo-submit-button')), findsNothing);
      },
    );

    testWidgets(
      'After a successful submit the app returns to the first screen, not the shift screen below',
      (tester) async {
        useDesignFrame(tester);
        final service = RecordingPhotosService(initialPhotos: allFivePhotos());
        Widget photos() => WorkerPhotosScreen(
          assignmentId: 2001,
          photosService: service,
          assignmentsService: FakeAssignmentsService(),
          camera: FakeCamera(jpeg),
        );
        await tester.pumpWidget(
          MaterialApp(
            home: Builder(
              builder: (home) => Scaffold(
                body: TextButton(
                  onPressed: () => Navigator.of(home).push(
                    MaterialPageRoute<void>(
                      builder: (shift) => Scaffold(
                        body: TextButton(
                          key: const Key('open-photos'),
                          onPressed: () => Navigator.of(shift).push(
                            MaterialPageRoute<void>(builder: (_) => photos()),
                          ),
                          child: const Text('shift screen'),
                        ),
                      ),
                    ),
                  ),
                  child: const Text('home'),
                ),
              ),
            ),
          ),
        );
        await tester.tap(find.text('home'));
        await tester.pumpAndSettle();
        await tester.tap(find.byKey(const Key('open-photos')));
        await tester.pumpAndSettle();

        await tapVisible(tester, find.text('Nghiệm thu'));
        await tester.pumpAndSettle();

        expect(service.submitCompletionCalls, 1);
        expect(find.text('home'), findsOneWidget);
        expect(find.text('shift screen'), findsNothing);
      },
    );
  });
}
