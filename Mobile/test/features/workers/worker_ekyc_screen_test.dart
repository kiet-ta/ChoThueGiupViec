import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_response.dart';
import 'package:mobile/features/workers/models/ekyc_models.dart';
import 'package:mobile/features/workers/screens/worker_ekyc_screen.dart';
import 'package:mobile/features/workers/services/workers_ekyc_service.dart';

import 'design_frame.dart';

class MockWorkersEkycService extends WorkersEkycService {
  SubmitEkycRequest? lastRequest;
  EkycResultResponse? responseToReturn;

  @override
  Future<ApiResponse<EkycResultResponse>> submitEkyc(
    SubmitEkycRequest request,
  ) async {
    lastRequest = request;
    return ApiResponse<EkycResultResponse>(
      success: true,
      message: 'OK',
      data:
          responseToReturn ??
          const EkycResultResponse(
            workerId: 101,
            confidenceScore: 92.0,
            kycStatus: 'APPROVED',
            autoApproved: true,
          ),
    );
  }
}

void main() {
  group('WorkerEkycScreen Tests', () {
    late MockWorkersEkycService mockService;

    setUp(() {
      mockService = MockWorkersEkycService();
    });

    testWidgets(
      'WorkerEkycScreen renders steps and navigates forward/backward',
      (tester) async {
        useDesignFrame(tester);
        await tester.pumpWidget(
          MaterialApp(home: WorkerEkycScreen(ekycService: mockService)),
        );

        // Initial state: Step 1 (Mặt Trước CCCD)
        expect(find.text('Xác minh danh tính'), findsOneWidget);
        expect(find.text('Mặt trước CCCD'), findsOneWidget);

        // Tap 'Tiếp theo' -> Step 2 (Mặt Sau CCCD)
        await tapVisible(tester, find.text('Tiếp theo'));
        await tester.pumpAndSettle();
        expect(find.text('Mặt sau CCCD'), findsOneWidget);

        // Tap 'Tiếp theo' -> Step 3 (Selfie)
        await tapVisible(tester, find.text('Tiếp theo'));
        await tester.pumpAndSettle();
        expect(find.text('Ảnh chân dung'), findsOneWidget);

        // Tap 'Tiếp theo' -> Step 4 (Summary & Confirm)
        await tapVisible(tester, find.text('Tiếp theo'));
        await tester.pumpAndSettle();
        expect(
          find.text('Xác nhận ảnh sẽ gửi', skipOffstage: false),
          findsOneWidget,
        );
        expect(find.text('Gửi xác minh'), findsOneWidget);
      },
    );

    testWidgets('WorkerEkycScreen submits eKYC and displays APPROVED result', (
      tester,
    ) async {
      useDesignFrame(tester);
      await tester.pumpWidget(
        MaterialApp(home: WorkerEkycScreen(ekycService: mockService)),
      );

      // Navigate to Step 4
      await tapVisible(tester, find.text('Tiếp theo'));
      await tester.pumpAndSettle();
      await tapVisible(tester, find.text('Tiếp theo'));
      await tester.pumpAndSettle();
      await tapVisible(tester, find.text('Tiếp theo'));
      await tester.pumpAndSettle();

      // Submit eKYC
      await tapVisible(tester, find.text('Gửi xác minh'));
      await tester.pumpAndSettle();

      expect(mockService.lastRequest, isNotNull);
      expect(
        mockService.lastRequest!.frontCccdUrl,
        contains('front_sample.jpg'),
      );
      expect(mockService.lastRequest!.backCccdUrl, contains('back_sample.jpg'));
      expect(mockService.lastRequest!.selfieUrl, contains('selfie_sample.jpg'));

      // Result screen rendered
      expect(find.text('Xác minh thành công!'), findsOneWidget);
      expect(find.text('92.0%'), findsOneWidget);
      expect(find.text('Đã duyệt'), findsOneWidget);
    });

    group('last step layout', () {
      Future<void> goToLastStep(WidgetTester tester) async {
        await tester.pumpWidget(
          MaterialApp(home: WorkerEkycScreen(ekycService: mockService)),
        );
        await tester.pumpAndSettle();
        // Focus a photo field first: its keyboard is what used to stay open.
        await tester.tap(find.byType(TextField).first);
        await tester.pumpAndSettle();
        for (var i = 0; i < 3; i++) {
          await tester.tap(find.text('Tiếp theo'));
          await tester.pumpAndSettle();
        }
      }

      for (final size in const [Size(360, 640), Size(390, 844)]) {
        testWidgets(
          'confirmation on top, send button under it at the bottom (${size.width.toInt()}x${size.height.toInt()})',
          (tester) async {
            tester.view.physicalSize = size;
            tester.view.devicePixelRatio = 1.0;
            addTearDown(() {
              tester.view.resetPhysicalSize();
              tester.view.resetDevicePixelRatio();
            });
            await goToLastStep(tester);

            final card = tester.getTopLeft(find.text('Xác nhận ảnh sẽ gửi')).dy;
            final send = tester.getTopLeft(find.text('Gửi xác minh')).dy;
            final edit = tester.getTopLeft(find.text('Chỉnh sửa ảnh')).dy;
            expect(card, lessThan(send));
            expect(send, lessThan(edit));
            // The buttons sit in the lower third of the screen, not up under the title.
            expect(send, greaterThan(size.height * 0.6));
          },
        );
      }

      testWidgets('changing step closes the keyboard of the photo field', (
        tester,
      ) async {
        useDesignFrame(tester);
        await tester.pumpWidget(
          MaterialApp(home: WorkerEkycScreen(ekycService: mockService)),
        );
        await tester.pumpAndSettle();
        await tester.tap(find.byType(TextField).first);
        await tester.pumpAndSettle();
        expect(tester.testTextInput.isVisible, isTrue);

        await tester.tap(find.text('Tiếp theo'));
        await tester.pumpAndSettle();

        expect(tester.testTextInput.isVisible, isFalse);
      });
    });
  });
}
