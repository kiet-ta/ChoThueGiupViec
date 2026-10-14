import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/widgets/nordic_text_input.dart';
import 'package:mobile/core/network/api_response.dart';
import 'package:mobile/features/workers/models/customer_acceptance_models.dart';
import 'package:mobile/features/workers/screens/customer_acceptance_screen.dart';
import 'package:mobile/features/workers/services/customer_acceptance_service.dart';

import 'design_frame.dart';

/// Accepts every redo request, so the redo test does not depend on a live backend (the test HTTP client answers 400).
class AcceptingRedoService extends CustomerAcceptanceService {
  @override
  Future<ApiResponse<AssignmentStatusDto>> requestRedo({
    required int assignmentId,
    required RequestRedoRequest request,
  }) async {
    return ApiResponse<AssignmentStatusDto>(
      success: true,
      message: 'OK',
      data: AssignmentStatusDto(
        assignmentId: assignmentId,
        status: 'IN_PROGRESS',
      ),
    );
  }
}

void main() {
  group('CustomerAcceptanceScreen Tests', () {
    testWidgets(
      'Renders title, status chip, angle selector, photo boxes, and action buttons',
      (tester) async {
        useDesignFrame(tester);
        await tester.pumpWidget(
          const MaterialApp(home: CustomerAcceptanceScreen(assignmentId: 2001)),
        );
        await tester.pumpAndSettle();

        expect(find.text('Nghiệm Thu Ca #2001'), findsOneWidget);
        expect(find.text('CHỜ NGHIỆM THU'), findsOneWidget);
        expect(find.text('Đối Chiếu Ảnh Before / After'), findsOneWidget);
        expect(find.text('Góc 1: Toàn cảnh phòng'), findsOneWidget);
        expect(find.text('TRƯỚC (Before)'), findsOneWidget);
        expect(find.text('SAU (After)'), findsOneWidget);
        expect(find.text('Xác Nhận Nghiệm Thu'), findsOneWidget);
        expect(find.text('Dọn Lại (15–30ph)'), findsOneWidget);
      },
    );

    testWidgets('Switching angles updates title in comparison card', (
      tester,
    ) async {
      useDesignFrame(tester);
      await tester.pumpWidget(
        const MaterialApp(home: CustomerAcceptanceScreen(assignmentId: 2001)),
      );
      await tester.pumpAndSettle();

      expect(find.text('Góc 1: Toàn cảnh phòng'), findsOneWidget);

      await tapVisible(tester, find.text('Góc 2'));
      await tester.pumpAndSettle();

      expect(find.text('Góc 2: Bàn ghế & tủ kệ'), findsOneWidget);
    });

    testWidgets(
      'Confirming acceptance updates status to COMPLETED and renders payout breakdown',
      (tester) async {
        useDesignFrame(tester);
        await tester.pumpWidget(
          const MaterialApp(home: CustomerAcceptanceScreen(assignmentId: 2001)),
        );
        await tester.pumpAndSettle();

        await tapVisible(tester, find.text('Xác Nhận Nghiệm Thu'));
        await tester.pumpAndSettle();

        expect(find.text('ĐÃ NGHIỆM THU'), findsOneWidget);
        expect(find.text('Chi Tiết Nghiệm Thu & Khóa Ca'), findsOneWidget);
        expect(find.text('Thu Nhập Thợ (80%):'), findsOneWidget);
        expect(find.text('208000 đ'), findsOneWidget);
      },
    );

    testWidgets(
      'Requesting redo toggles form, validates reason, and updates status to IN_PROGRESS',
      (tester) async {
        useDesignFrame(tester);
        await tester.pumpWidget(
          MaterialApp(
            home: CustomerAcceptanceScreen(
              assignmentId: 2001,
              acceptanceService: AcceptingRedoService(),
            ),
          ),
        );
        await tester.pumpAndSettle();

        await tapVisible(tester, find.text('Dọn Lại (15–30ph)'));
        await tester.pumpAndSettle();

        expect(find.text('Yêu Cầu Dọn Lại (15–30 phút)'), findsOneWidget);

        // Try submit empty reason
        await tapVisible(tester, find.text('Gửi Yêu Cầu Dọn Lại'));
        await tester.pumpAndSettle();

        await scrollTo(
          tester,
          find.text('Vui lòng nhập lý do yêu cầu dọn lại.'),
        );

        expect(
          find.text('Vui lòng nhập lý do yêu cầu dọn lại.'),
          findsOneWidget,
        );

        // NordicTextInput draws its label above the TextField, so find the field inside the labelled input.
        final reasonField = find.descendant(
          of: find.widgetWithText(
            NordicTextInput,
            'Nội dung / Lý do yêu cầu dọn lại',
          ),
          matching: find.byType(TextField),
        );
        // Enter reason
        await scrollTo(tester, reasonField);
        await tester.enterText(reasonField, 'Chưa lau bụi dưới gầm bàn');
        await tester.pumpAndSettle();

        await tapVisible(tester, find.text('Gửi Yêu Cầu Dọn Lại'));
        await tester.pumpAndSettle();

        await scrollTo(tester, find.text('ĐANG DỌN LẠI'));

        expect(find.text('ĐANG DỌN LẠI'), findsOneWidget);
        expect(find.textContaining('Đã gửi yêu cầu dọn lại'), findsOneWidget);
      },
    );
  });
}
