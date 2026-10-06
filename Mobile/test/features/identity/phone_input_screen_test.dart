import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/models/user_role.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/identity/screens/otp_verify_screen.dart';
import 'package:mobile/features/identity/screens/phone_input_screen.dart';
import 'package:mobile/features/identity/services/auth_service.dart';

void main() {
  group('PhoneInputScreen Widget Tests', () {
    testWidgets('renders phone input screen and validates phone', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: PhoneInputScreen(role: AppRole.customer, authService: AuthService(useMockFallback: true)),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Đăng nhập bằng số điện thoại'), findsOneWidget);
      expect(find.text('Gửi Mã Xác Thực'), findsOneWidget);

      // Tap submit with empty phone -> shows error
      await tester.tap(find.text('Gửi Mã Xác Thực'));
      await tester.pumpAndSettle();

      expect(find.textContaining('Vui lòng nhập số điện thoại'), findsOneWidget);

      // Enter valid phone
      await tester.enterText(find.byType(TextField), '0912345678');
      await tester.pumpAndSettle();

      // Tap submit -> navigates to OtpVerifyScreen
      await tester.tap(find.text('Gửi Mã Xác Thực'));
      await tester.pumpAndSettle();

      expect(find.byType(OtpVerifyScreen), findsOneWidget);
      expect(find.text('Nhập mã xác thực'), findsOneWidget);
    });

    testWidgets('displays worker role indicator when role is Worker', (tester) async {
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
  });
}
