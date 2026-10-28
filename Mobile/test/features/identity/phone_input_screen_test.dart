import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/models/user_role.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/identity/screens/otp_verify_screen.dart';
import 'package:mobile/features/identity/screens/phone_input_screen.dart';
import 'package:mobile/features/identity/services/auth_service.dart';

void main() {
  group('PhoneInputScreen Widget Tests', () {
    testWidgets('renders phone input screen and validates phone', (
      tester,
    ) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: PhoneInputScreen(
            role: AppRole.customer,
            authService: AuthService(useMockFallback: true),
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Số điện thoại'), findsOneWidget);
      expect(find.text('Gửi Mã Xác Thực'), findsOneWidget);

      // Tap submit with empty phone -> shows error
      await tester.tap(find.text('Gửi Mã Xác Thực'));
      await tester.pumpAndSettle();

      expect(
        find.textContaining('Vui lòng nhập số điện thoại'),
        findsOneWidget,
      );

      // Enter valid phone
      await tester.enterText(find.byType(TextField), '0912345678');
      await tester.pumpAndSettle();

      // Tap submit -> navigates to OtpVerifyScreen
      await tester.tap(find.text('Gửi Mã Xác Thực'));
      await tester.pumpAndSettle();

      expect(find.byType(OtpVerifyScreen), findsOneWidget);
      expect(find.text('Nhập mã xác thực'), findsOneWidget);
    });

    testWidgets('displays worker role indicator when role is Worker', (
      tester,
    ) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: const PhoneInputScreen(role: AppRole.worker),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Vai trò: Chuyên Viên / Thợ'), findsOneWidget);
    });

    String photoOf(WidgetTester tester) {
      final image = tester.widget<Image>(find.byKey(const Key('auth-photo')));
      return (image.image as AssetImage).assetName;
    }

    testWidgets('the header shows the welcome photo of the role', (
      tester,
    ) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(tester.view.reset);

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: const PhoneInputScreen(role: AppRole.customer),
        ),
      );
      await tester.pumpAndSettle();
      expect(photoOf(tester), 'assets/images/welcome/mop-living-room.jpg');
      expect(find.byKey(const Key('phone-input-title')), findsOneWidget);
      expect(find.text('Mã 6 số sẽ gửi qua SMS.'), findsOneWidget);
      // First screen of the app: nothing to go back to.
      expect(find.byKey(const Key('auth-back')), findsNothing);

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: const PhoneInputScreen(
            key: Key('worker'),
            role: AppRole.worker,
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(photoOf(tester), 'assets/images/welcome/caddy-stairs.jpg');
      expect(tester.takeException(), isNull);
    });

    testWidgets('the back button returns to the screen before', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(tester.view.reset);

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: Builder(
            builder: (context) => TextButton(
              onPressed: () => Navigator.push(
                context,
                MaterialPageRoute(builder: (_) => const PhoneInputScreen()),
              ),
              child: const Text('open'),
            ),
          ),
        ),
      );
      await tester.tap(find.text('open'));
      await tester.pumpAndSettle();
      expect(find.byType(PhoneInputScreen), findsOneWidget);

      await tester.tap(find.byKey(const Key('auth-back')));
      await tester.pumpAndSettle();
      expect(find.byType(PhoneInputScreen), findsNothing);
      expect(find.text('open'), findsOneWidget);
    });

    testWidgets('fits a short screen with the keyboard open', (tester) async {
      tester.view.physicalSize = const Size(360, 640);
      tester.view.devicePixelRatio = 1.0;
      tester.view.viewInsets = const FakeViewPadding(bottom: 280);
      addTearDown(tester.view.reset);

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: const PhoneInputScreen(),
        ),
      );
      await tester.pumpAndSettle();

      expect(tester.takeException(), isNull);
      await tester.ensureVisible(find.byType(TextField));
      await tester.pumpAndSettle();
      expect(find.byType(TextField).hitTestable(), findsOneWidget);
    });
  });
}
