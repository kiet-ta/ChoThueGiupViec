import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_response.dart';
import 'package:mobile/features/workers/models/job_photo_models.dart';
import 'package:mobile/features/workers/screens/worker_photos_screen.dart';
import 'package:mobile/features/workers/services/workers_photos_service.dart';

import 'design_frame.dart';

/// Accepts every upload, so the sharp-image test does not depend on a live backend (the test HTTP client answers 400).
class AcceptingPhotosService extends WorkersPhotosService {
  @override
  Future<ApiResponse<JobPhotoDto>> uploadPhoto({
    required int assignmentId,
    required UploadJobPhotoRequest request,
  }) async {
    return ApiResponse<JobPhotoDto>(
      success: true,
      message: 'OK',
      data: JobPhotoDto(
        photoId: 9001,
        assignmentId: assignmentId,
        photoPhase: request.photoPhase,
        angleNo: request.angleNo,
        photoUrl: request.photoUrl,
        volScore: 142.5,
        isAccepted: true,
        uploadedAt: DateTime(2026, 10, 13, 9),
      ),
    );
  }
}

void main() {
  group('WorkerPhotosScreen Tests', () {
    testWidgets(
      'Renders header, phase selector, angle selector, camera guide, and submit button',
      (tester) async {
        useDesignFrame(tester);
        await tester.pumpWidget(
          const MaterialApp(home: WorkerPhotosScreen(assignmentId: 2001)),
        );
        await tester.pumpAndSettle();

        expect(find.text('Ảnh Before/After #2001'), findsOneWidget);
        expect(find.text('KIỂM TRA CHẤT LƯỢNG VOL'), findsOneWidget);
        expect(find.text('BEFORE (Trước)'), findsOneWidget);
        expect(find.text('AFTER (Sau)'), findsOneWidget);
        expect(find.text('Góc 1'), findsOneWidget);
        expect(find.text('Góc 1: Toàn cảnh'), findsOneWidget);
        expect(find.text('Gửi Ảnh Chụp'), findsOneWidget);
      },
    );

    testWidgets('Switching angles updates camera guide text', (tester) async {
      useDesignFrame(tester);
      await tester.pumpWidget(
        const MaterialApp(home: WorkerPhotosScreen(assignmentId: 2001)),
      );
      await tester.pumpAndSettle();

      expect(find.text('Góc 1: Toàn cảnh'), findsOneWidget);

      await tapVisible(tester, find.text('Góc 2'));
      await tester.pumpAndSettle();

      expect(find.text('Góc 2: Bàn ghế & tủ'), findsOneWidget);
    });

    testWidgets(
      'Uploading blurred image triggers VoL threshold alert and retake prompt',
      (tester) async {
        useDesignFrame(tester);
        await tester.pumpWidget(
          const MaterialApp(
            home: WorkerPhotosScreen(assignmentId: 2001, volThreshold: 100.0),
          ),
        );
        await tester.pumpAndSettle();

        // Tap test retake button which sets blurry URL
        await tapVisible(tester, find.text('Tải Ảnh Mờ (Test retake)'));
        await tester.pumpAndSettle();

        // Tap submit
        await tapVisible(tester, find.text('Gửi Ảnh Chụp'));
        await tester.pumpAndSettle();

        expect(find.textContaining('Ảnh bị mờ'), findsOneWidget);
        expect(find.textContaining('100.0'), findsOneWidget);
      },
    );

    testWidgets(
      'Uploading sharp image passes VoL check and shows success prompt',
      (tester) async {
        useDesignFrame(tester);
        await tester.pumpWidget(
          MaterialApp(
            home: WorkerPhotosScreen(
              assignmentId: 2001,
              photosService: AcceptingPhotosService(),
              volThreshold: 100.0,
            ),
          ),
        );
        await tester.pumpAndSettle();

        // Tap submit with default sharp URL
        await tapVisible(tester, find.text('Gửi Ảnh Chụp'));
        await tester.pumpAndSettle();

        await scrollTo(tester, find.textContaining('Tải ảnh thành công'));

        expect(find.textContaining('Tải ảnh thành công'), findsOneWidget);
        expect(find.textContaining('ĐẠT CHUẨN'), findsOneWidget);
      },
    );
  });
}
