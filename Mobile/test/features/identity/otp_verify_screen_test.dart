import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/models/user_role.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/identity/screens/otp_verify_screen.dart';
import 'package:mobile/features/identity/services/auth_service.dart';
import 'package:mobile/features/identity/services/token_storage.dart';

void main() {
  group('OtpVerifyScreen Widget Tests', () {
    late TokenStorage storage;
    late AuthService authService;

    setUp(() {
      storage = TokenStorage();
      storage.clear();
      authService = AuthService(tokenStorage: storage, useMockFallback: true);
    });

    testWidgets('renders verify screen elements and handles submit', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: OtpVerifyScreen(
            phoneNumber: '0912345678',
            role: AppRole.customer,
            authService: authService,
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Nhập mã xác thực'), findsOneWidget);
      expect(find.textContaining('0912345678'), findsOneWidget);
      expect(find.text('Xác Nhận Đăng Nhập'), findsOneWidget);

      // Tap submit with empty OTP -> shows error
      await tester.tap(find.text('Xác Nhận Đăng Nhập'));
      await tester.pumpAndSettle();
      expect(find.textContaining('Vui lòng nhập đủ 6 chữ số'), findsOneWidget);

      // Enter valid OTP 123456
      await tester.enterText(find.byType(TextField), '123456');
      await tester.pumpAndSettle();

      // Tap submit -> succeeds and stores token
      await tester.tap(find.text('Xác Nhận Đăng Nhập'));
      await tester.pumpAndSettle();

      expect(storage.isAuthenticated, isTrue);
    });

    testWidgets('shows error banner on invalid OTP 000000', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: OtpVerifyScreen(
            phoneNumber: '0912345678',
            role: AppRole.customer,
            authService: authService,
          ),
        ),
      );
      await tester.pumpAndSettle();

      await tester.enterText(find.byType(TextField), '000000');
      await tester.pumpAndSettle();

      await tester.tap(find.text('Xác Nhận Đăng Nhập'));
      await tester.pumpAndSettle();

      expect(find.textContaining('Mã xác thực không chính xác'), findsOneWidget);
      expect(storage.isAuthenticated, isFalse);
    });
  });
}
