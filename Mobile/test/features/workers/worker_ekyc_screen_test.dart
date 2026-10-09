import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_response.dart';
import 'package:mobile/features/workers/models/ekyc_models.dart';
import 'package:mobile/features/workers/screens/worker_ekyc_screen.dart';
import 'package:mobile/features/workers/services/workers_ekyc_service.dart';

class MockWorkersEkycService extends WorkersEkycService {
  SubmitEkycRequest? lastRequest;
  EkycResultResponse? responseToReturn;

  @override
  Future<ApiResponse<EkycResultResponse>> submitEkyc(SubmitEkycRequest request) async {
    lastRequest = request;
    return ApiResponse<EkycResultResponse>(
      success: true,
      message: 'OK',
      data: responseToReturn ??
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

    testWidgets('WorkerEkycScreen renders steps and navigates forward/backward', (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          home: WorkerEkycScreen(ekycService: mockService),
        ),
      );

      // Initial state: Step 1 (Mặt Trước CCCD)
      expect(find.text('Xác Thực eKYC'), findsOneWidget);
      expect(find.text('Mặt Trước CCCD'), findsOneWidget);

      // Tap 'Tiếp theo' -> Step 2 (Mặt Sau CCCD)
      await tester.tap(find.text('Tiếp theo'));
      await tester.pumpAndSettle();
      expect(find.text('Mặt Sau CCCD'), findsOneWidget);

      // Tap 'Tiếp theo' -> Step 3 (Selfie)
      await tester.tap(find.text('Tiếp theo'));
      await tester.pumpAndSettle();
      expect(find.text('Ảnh Chân Dung (Selfie)'), findsOneWidget);

      // Tap 'Tiếp theo' -> Step 4 (Summary & Confirm)
      await tester.tap(find.text('Tiếp theo'));
      await tester.pumpAndSettle();
      expect(find.text('Xác Nhận Thông Tin Ảnh eKYC'), findsOneWidget);
      expect(find.text('Gửi Xác Thực eKYC'), findsOneWidget);
    });

    testWidgets('WorkerEkycScreen submits eKYC and displays APPROVED result', (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          home: WorkerEkycScreen(ekycService: mockService),
        ),
      );

      // Navigate to Step 4
      await tester.tap(find.text('Tiếp theo'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Tiếp theo'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Tiếp theo'));
      await tester.pumpAndSettle();

      // Submit eKYC
      await tester.tap(find.text('Gửi Xác Thực eKYC'));
      await tester.pumpAndSettle();

      expect(mockService.lastRequest, isNotNull);
      expect(mockService.lastRequest!.frontCccdUrl, contains('front_sample.jpg'));
      expect(mockService.lastRequest!.backCccdUrl, contains('back_sample.jpg'));
      expect(mockService.lastRequest!.selfieUrl, contains('selfie_sample.jpg'));

      // Result screen rendered
      expect(find.text('Xác Thực Thành Công!'), findsOneWidget);
      expect(find.text('92.0%'), findsOneWidget);
      expect(find.text('APPROVED'), findsOneWidget);
    });
  });
}
