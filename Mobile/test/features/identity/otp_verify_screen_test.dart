import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/models/user_role.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/identity/models/auth_models.dart';
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

    testWidgets('renders verify screen elements and handles submit', (
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
          home: OtpVerifyScreen(
            phoneNumber: '0912345678',
            role: AppRole.customer,
            authService: authService,
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Nhập mã xác nhận'), findsOneWidget);
      expect(find.textContaining('0912345678'), findsOneWidget);
      expect(find.text('Đăng nhập'), findsOneWidget);

      // Tap submit with empty OTP -> shows error
      await tester.tap(find.text('Đăng nhập'));
      await tester.pumpAndSettle();
      expect(find.textContaining('Nhập đủ 6 số'), findsOneWidget);

      // Enter valid OTP 123456
      await tester.enterText(find.byType(TextField), '123456');
      await tester.pumpAndSettle();

      // Tap submit -> succeeds and stores token
      await tester.tap(find.text('Đăng nhập'));
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

      await tester.tap(find.text('Đăng nhập'));
      await tester.pumpAndSettle();

      expect(
        find.textContaining('Mã xác thực không chính xác'),
        findsOneWidget,
      );
      expect(storage.isAuthenticated, isFalse);
    });

    testWidgets('a wrong code shakes the cells with a haptic', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(tester.view.reset);
      final haptics = <String>[];
      tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
        SystemChannels.platform,
        (call) async {
          if (call.method == 'HapticFeedback.vibrate') {
            haptics.add('${call.arguments}');
          }
          return null;
        },
      );
      addTearDown(
        () => tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
          SystemChannels.platform,
          null,
        ),
      );
      double shakeX() => tester
          .widget<Transform>(
            find
                .descendant(
                  of: find.byKey(const Key('otp-shake')),
                  matching: find.byType(Transform),
                )
                .first,
          )
          .transform
          .getTranslation()
          .x;

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: OtpVerifyScreen(
            phoneNumber: '0912345678',
            authService: authService,
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(shakeX(), 0.0);

      // The 6th digit submits on its own (unchanged); the mock rejects 000000.
      await tester.enterText(find.byType(TextField), '000000');
      await tester.pump();
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 20));
      expect(shakeX().abs(), greaterThan(1.0));
      expect(haptics, contains('HapticFeedbackType.heavyImpact'));

      await tester.pumpAndSettle();
      expect(shakeX().abs(), lessThan(0.01));
      expect(
        find.textContaining('Mã xác thực không chính xác'),
        findsOneWidget,
      );
    });

    testWidgets('the confirm button stays above the keyboard', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      tester.view.viewInsets = const FakeViewPadding(bottom: 300);
      addTearDown(tester.view.reset);

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: OtpVerifyScreen(
            phoneNumber: '0912345678',
            authService: authService,
          ),
        ),
      );
      await tester.pumpAndSettle();

      final button = find.text('Đăng nhập');
      expect(tester.getRect(button).bottom, lessThanOrEqualTo(844 - 300));
      expect(button.hitTestable(), findsOneWidget);
    });

    testWidgets('after a wrong code the digits can be deleted and retyped', (
      tester,
    ) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(tester.view.reset);

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: OtpVerifyScreen(
            phoneNumber: '0912345678',
            authService: _CountingAuthService(storage),
          ),
        ),
      );
      await tester.pumpAndSettle();

      // The 6th digit submits on its own; the mock rejects 000000.
      await tester.enterText(find.byType(TextField), '000000');
      await tester.pumpAndSettle();
      expect(
        find.textContaining('Mã xác thực không chính xác'),
        findsOneWidget,
      );

      // The hidden input keeps the focus, so backspace still reaches it.
      final editable = tester.state<EditableTextState>(
        find.byType(EditableText),
      );
      expect(editable.widget.focusNode.hasFocus, isTrue);
      await tester.sendKeyEvent(LogicalKeyboardKey.backspace);
      await tester.pump();
      expect(
        tester.widget<TextField>(find.byType(TextField)).controller!.text,
        '00000',
      );
      expect(find.textContaining('Mã xác thực không chính xác'), findsNothing);

      // Typing through the open keyboard connection runs the check again.
      tester.testTextInput.enterText('000001');
      await tester.pumpAndSettle();
      expect(storage.isAuthenticated, isTrue);
    });

    testWidgets('a code being checked is not submitted twice', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(tester.view.reset);
      final service = _CountingAuthService(storage);

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: OtpVerifyScreen(
            phoneNumber: '0912345678',
            authService: service,
          ),
        ),
      );
      await tester.pumpAndSettle();

      await tester.enterText(find.byType(TextField), '000000');
      await tester.pump();
      // Still checking (the button shows a spinner): a re-typed 6th digit must not send again.
      tester.testTextInput.enterText('00000');
      tester.testTextInput.enterText('000000');
      await tester.pumpAndSettle();
      expect(service.verifyCalls, 1);
    });
  });
}

class _CountingAuthService extends AuthService {
  _CountingAuthService(TokenStorage storage)
    : super(tokenStorage: storage, useMockFallback: true);

  int verifyCalls = 0;

  @override
  Future<VerifyOtpResponse> verifyOtp({
    required String phoneNumber,
    required AppRole role,
    required String code,
  }) async {
    verifyCalls++;
    await Future<void>.delayed(const Duration(milliseconds: 200));
    return super.verifyOtp(phoneNumber: phoneNumber, role: role, code: code);
  }
}
