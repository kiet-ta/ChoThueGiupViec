import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/dispatch/services/dispatch_service.dart';
import 'package:mobile/features/dispatch/screens/dispatch_offers_screen.dart';

void main() {
  group('DispatchOffersScreen Widget Tests', () {
    late DispatchService service;

    setUp(() {
      service = DispatchService(isMock: true);
    });

    Widget createScreen() {
      return MaterialApp(
        theme: NordicTheme.lightTheme,
        home: DispatchOffersScreen(dispatchService: service),
      );
    }

    testWidgets('displays job offer card with district, net earnings and countdown', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      await tester.pumpWidget(createScreen());
      await tester.pumpAndSettle();

      // District
      expect(find.text('Quận Cầu Giấy'), findsOneWidget);
      // Net earnings header & amount (208.000 đ)
      expect(find.text('THỰC NHẬN SAU HOA HỒNG'), findsOneWidget);
      expect(find.text('208.000 đ'), findsOneWidget);
      // Commission 20%
      expect(find.text('Hoa hồng 20%'), findsOneWidget);
      // Action buttons
      expect(find.byKey(const Key('accept_offer_button')), findsOneWidget);
      expect(find.byKey(const Key('decline_offer_button')), findsOneWidget);
    });

    testWidgets('tapping Accept invokes acceptOffer and shows success message', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      await tester.pumpWidget(createScreen());
      await tester.pumpAndSettle();

      final acceptButton = find.byKey(const Key('accept_offer_button'));
      expect(acceptButton, findsOneWidget);

      await tester.tap(acceptButton);
      await tester.pumpAndSettle();

      expect(find.textContaining('Nhận cuốc thành công! Mã ca làm #1001'), findsOneWidget);
    });

    testWidgets('tapping Decline invokes declineOffer and shows decline message', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      await tester.pumpWidget(createScreen());
      await tester.pumpAndSettle();

      final declineButton = find.byKey(const Key('decline_offer_button'));
      expect(declineButton, findsOneWidget);

      await tester.tap(declineButton);
      await tester.pumpAndSettle();

      expect(find.text('Đã bỏ qua ca làm việc.'), findsOneWidget);
    });

    testWidgets('shows empty state when no pending offer exists', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      service.setMockOffer(null);

      await tester.pumpWidget(createScreen());
      await tester.pumpAndSettle();

      expect(find.text('Chưa có cuốc mới'), findsOneWidget);
      expect(find.textContaining('Hệ thống đang quét'), findsOneWidget);
    });
  });
}
