import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/workers/screens/customer_acceptance_screen.dart';

void main() {
  group('CustomerAcceptanceScreen Tests', () {
    testWidgets('Renders title, status chip, angle selector, photo boxes, and action buttons', (tester) async {
      await tester.pumpWidget(
        const MaterialApp(
          home: CustomerAcceptanceScreen(
            assignmentId: 2001,
          ),
        ),
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
    });

    testWidgets('Switching angles updates title in comparison card', (tester) async {
      await tester.pumpWidget(
        const MaterialApp(
          home: CustomerAcceptanceScreen(
            assignmentId: 2001,
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Góc 1: Toàn cảnh phòng'), findsOneWidget);

      await tester.tap(find.text('Góc 2'));
      await tester.pumpAndSettle();

      expect(find.text('Góc 2: Bàn ghế & tủ kệ'), findsOneWidget);
    });

    testWidgets('Confirming acceptance updates status to COMPLETED and renders payout breakdown', (tester) async {
      await tester.pumpWidget(
        const MaterialApp(
          home: CustomerAcceptanceScreen(
            assignmentId: 2001,
          ),
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.text('Xác Nhận Nghiệm Thu'));
      await tester.pumpAndSettle();

      expect(find.text('ĐÃ NGHIỆM THU'), findsOneWidget);
      expect(find.text('Chi Tiết Nghiệm Thu & Khóa Ca'), findsOneWidget);
      expect(find.text('Thu Nhập Thợ (80%):'), findsOneWidget);
      expect(find.text('208000 đ'), findsOneWidget);
    });

    testWidgets('Requesting redo toggles form, validates reason, and updates status to IN_PROGRESS', (tester) async {
      await tester.pumpWidget(
        const MaterialApp(
          home: CustomerAcceptanceScreen(
            assignmentId: 2001,
          ),
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.text('Dọn Lại (15–30ph)'));
      await tester.pumpAndSettle();

      expect(find.text('Yêu Cầu Dọn Lại (15–30 phút)'), findsOneWidget);

      // Try submit empty reason
      await tester.tap(find.text('Gửi Yêu Cầu Dọn Lại'));
      await tester.pumpAndSettle();

      expect(find.text('Vui lòng nhập lý do yêu cầu dọn lại.'), findsOneWidget);

      // Enter reason
      await tester.enterText(
        find.widgetWithText(TextField, 'Nội dung / Lý do yêu cầu dọn lại'),
        'Chưa lau bụi dưới gầm bàn',
      );
      await tester.pumpAndSettle();

      await tester.tap(find.text('Gửi Yêu Cầu Dọn Lại'));
      await tester.pumpAndSettle();

      expect(find.text('ĐANG DỌN LẠI'), findsOneWidget);
      expect(find.textContaining('Đã gửi yêu cầu dọn lại'), findsOneWidget);
    });
  });
}
