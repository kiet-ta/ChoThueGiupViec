import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/models/user_role.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/identity/screens/otp_verify_screen.dart';
import 'package:mobile/features/identity/screens/phone_input_screen.dart';
import 'package:mobile/features/identity/services/auth_service.dart';
import 'package:mobile/features/identity/widgets/auth_photo_header.dart';

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

      expect(find.text('Nhập số điện thoại'), findsOneWidget);
      expect(find.text('Nhận mã'), findsOneWidget);

      // Tap submit with empty phone -> shows error
      await tester.tap(find.text('Nhận mã'));
      await tester.pumpAndSettle();

      expect(find.textContaining('Số điện thoại chưa đúng'), findsOneWidget);

      // Enter valid phone
      await tester.enterText(find.byType(TextField), '0912345678');
      await tester.pumpAndSettle();

      // Tap submit -> navigates to OtpVerifyScreen
      await tester.tap(find.text('Nhận mã'));
      await tester.pumpAndSettle();

      expect(find.byType(OtpVerifyScreen), findsOneWidget);
      expect(find.text('Nhập mã xác nhận'), findsOneWidget);
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

      expect(find.text('Người giúp việc'), findsOneWidget);
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
      expect(find.text('Mã xác nhận gửi qua tin nhắn.'), findsOneWidget);
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

    testWidgets('the send button stays above the keyboard', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      tester.view.viewInsets = const FakeViewPadding(bottom: 300);
      addTearDown(tester.view.reset);

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: const PhoneInputScreen(),
        ),
      );
      await tester.pumpAndSettle();

      final button = find.text('Nhận mã');
      expect(tester.getRect(button).bottom, lessThanOrEqualTo(844 - 300));
      expect(button.hitTestable(), findsOneWidget);
    });

    testWidgets('a check mark tells a valid number while typing', (
      tester,
    ) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(tester.view.reset);

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: const PhoneInputScreen(),
        ),
      );
      await tester.pumpAndSettle();
      double markScale() => tester
          .widget<AnimatedScale>(find.byKey(const Key('phone-valid-mark')))
          .scale;

      await tester.enterText(find.byType(TextField), '091234');
      await tester.pumpAndSettle();
      expect(markScale(), 0.0);

      await tester.enterText(find.byType(TextField), '0912345678');
      await tester.pumpAndSettle();
      expect(markScale(), 1.0);
      // Nothing was submitted: no error message yet.
      expect(find.textContaining('chưa đúng'), findsNothing);
    });

    testWidgets('the form sheet rises into place, or only fades when motion '
        'is reduced', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(tester.view.reset);

      double sheetY() => tester
          .widget<Transform>(
            find
                .descendant(
                  of: find.byType(AuthFormSheet),
                  matching: find.byType(Transform),
                )
                .first,
          )
          .transform
          .getTranslation()
          .y;

      Widget app({required bool reduced}) => MaterialApp(
        theme: NordicTheme.lightTheme,
        builder: (context, page) => MediaQuery(
          data: MediaQuery.of(context).copyWith(disableAnimations: reduced),
          child: page!,
        ),
        home: PhoneInputScreen(key: ValueKey(reduced)),
      );

      await tester.pumpWidget(app(reduced: false));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 16));
      expect(sheetY(), greaterThan(-AuthPhotoHeader.sheetOverlap + 20));
      await tester.pumpAndSettle();
      expect(sheetY(), closeTo(-AuthPhotoHeader.sheetOverlap, 0.1));

      await tester.pumpWidget(app(reduced: true));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 16));
      expect(sheetY(), -AuthPhotoHeader.sheetOverlap);
      await tester.pumpAndSettle();
    });

    testWidgets('fills the full width of a wider phone, no side strips', (
      tester,
    ) async {
      tester.view.physicalSize = const Size(428, 926);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(tester.view.reset);

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: const PhoneInputScreen(),
        ),
      );
      await tester.pumpAndSettle();

      for (final part in [AuthPhotoHeader, AuthFormSheet]) {
        final rect = tester.getRect(find.byType(part));
        expect(rect.left, 0.0, reason: '$part left edge');
        expect(rect.right, 428.0, reason: '$part right edge');
      }
    });
  });
}
