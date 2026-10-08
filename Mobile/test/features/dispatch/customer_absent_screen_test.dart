import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/dispatch/screens/customer_absent_screen.dart';
import 'package:mobile/features/dispatch/services/dispatch_service.dart';

void main() {
  group('CustomerAbsentScreen Widget Tests', () {
    late DispatchService service;

    setUp(() {
      service = DispatchService(isMock: true);
    });

    Widget createScreen({required DateTime checkedInAt}) {
      return MaterialApp(
        theme: NordicTheme.lightTheme,
        home: CustomerAbsentScreen(
          assignmentId: 1001,
          checkedInAt: checkedInAt,
          customerPhoneMasked: '090***4567',
          dispatchService: service,
        ),
      );
    }

    testWidgets('shows disabled submit button when conditions not met (<15 mins, <2 calls)', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      // Checked in 5 minutes ago, 0 calls
      final checkedInRecently = DateTime.now().subtract(const Duration(minutes: 5));
      service.setMockAbsentPreconditions(callAttempts: 0, checkedInAt: checkedInRecently);

      await tester.pumpWidget(createScreen(checkedInAt: checkedInRecently));
      await tester.pumpAndSettle();

      expect(find.text('Báo Khách Vắng Mặt'), findsOneWidget);
      expect(find.text('Chưa đủ điều kiện'), findsOneWidget);
      expect(find.byKey(const Key('call_customer_button')), findsOneWidget);
    });

    testWidgets('calling customer increments call count', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      final checkedInRecently = DateTime.now().subtract(const Duration(minutes: 5));
      service.setMockAbsentPreconditions(callAttempts: 0, checkedInAt: checkedInRecently);

      await tester.pumpWidget(createScreen(checkedInAt: checkedInRecently));
      await tester.pumpAndSettle();

      // First call
      await tester.tap(find.byKey(const Key('call_customer_button')));
      await tester.pumpAndSettle();
      expect(find.text('Số cuộc gọi đã thực hiện: 1 / 2'), findsOneWidget);

      // Second call
      await tester.tap(find.byKey(const Key('call_customer_button')));
      await tester.pumpAndSettle();
      expect(find.text('Số cuộc gọi đã thực hiện: 2 / 2'), findsOneWidget);
    });

    testWidgets('submitting absent report succeeds when wait >= 15m and calls >= 2', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      // Checked in 18 minutes ago
      final checkedInEligible = DateTime.now().subtract(const Duration(minutes: 18));
      service.setMockAbsentPreconditions(callAttempts: 2, checkedInAt: checkedInEligible);

      await tester.pumpWidget(createScreen(checkedInAt: checkedInEligible));
      await tester.pumpAndSettle();

      // Log 2 calls on screen
      await tester.tap(find.byKey(const Key('call_customer_button')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('call_customer_button')));
      await tester.pumpAndSettle();

      // Submit button should now be active
      final submitButton = find.byKey(const Key('report_absent_button'));
      expect(submitButton, findsOneWidget);
      expect(find.textContaining('Báo Vắng Mặt (40%)'), findsOneWidget);

      await tester.tap(submitButton);
      await tester.pumpAndSettle();

      expect(find.text('Đã Gửi Báo Cáo Khách Vắng Mặt'), findsOneWidget);
      expect(find.textContaining('104.000 đ'), findsOneWidget);
      expect(find.text('40%'), findsOneWidget);
    });
  });
}
