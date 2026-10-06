import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/customers/screens/customer_profile_screen.dart';
import 'package:mobile/features/customers/services/customer_profile_service.dart';
import 'package:mobile/features/identity/services/token_storage.dart';

void main() {
  group('CustomerProfileScreen Widget Tests', () {
    late CustomerProfileService service;
    late TokenStorage tokenStorage;

    setUp(() {
      service = CustomerProfileService(isMock: true);
      tokenStorage = TokenStorage();
    });

    testWidgets('renders customer profile, badges, and form fields', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: CustomerProfileScreen(
            service: service,
            tokenStorage: tokenStorage,
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Hồ Sơ Cá Nhân'), findsOneWidget);
      expect(find.text('Nguyễn Văn An'), findsWidgets);
      expect(find.text('0901234567'), findsWidgets);
      expect(find.text('Hoạt động'), findsOneWidget);
      expect(find.textContaining('5.0 • Xuất sắc'), findsOneWidget);
      expect(find.text('Lưu Thay Đổi'), findsOneWidget);
      expect(find.text('Đăng Xuất'), findsOneWidget);
    });

    testWidgets('edits full name, submits, and shows success snackbar', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: CustomerProfileScreen(
            service: service,
            tokenStorage: tokenStorage,
          ),
        ),
      );
      await tester.pumpAndSettle();

      final nameField = find.byKey(const Key('fullNameField'));
      await tester.tap(nameField);
      await tester.enterText(nameField, 'Nguyễn Kiệt Mới');
      await tester.pumpAndSettle();

      final saveButton = find.byKey(const Key('saveProfileButton'));
      await tester.ensureVisible(saveButton);
      await tester.pumpAndSettle();
      await tester.tap(saveButton);
      await tester.pumpAndSettle();

      expect(find.text('Cập nhật hồ sơ thành công!'), findsOneWidget);
      expect(find.text('Nguyễn Kiệt Mới'), findsWidgets);
    });

    testWidgets('validates empty full name and shows error message', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: CustomerProfileScreen(
            service: service,
            tokenStorage: tokenStorage,
          ),
        ),
      );
      await tester.pumpAndSettle();

      final nameField = find.byKey(const Key('fullNameField'));
      await tester.tap(nameField);
      await tester.enterText(nameField, '   ');
      await tester.pumpAndSettle();

      final saveButton = find.byKey(const Key('saveProfileButton'));
      await tester.ensureVisible(saveButton);
      await tester.pumpAndSettle();
      await tester.tap(saveButton);
      await tester.pumpAndSettle();

      expect(find.text('Vui lòng nhập họ và tên'), findsOneWidget);
    });

    testWidgets('triggers logout dialog when tapping logout button', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: CustomerProfileScreen(
            service: service,
            tokenStorage: tokenStorage,
          ),
        ),
      );
      await tester.pumpAndSettle();

      final logoutButton = find.byKey(const Key('logoutButton'));
      await tester.ensureVisible(logoutButton);
      await tester.pumpAndSettle();
      await tester.tap(logoutButton);
      await tester.pumpAndSettle();

      expect(find.text('Đăng xuất tài khoản'), findsOneWidget);
      expect(find.text('Huỷ'), findsOneWidget);
      expect(find.text('Đăng xuất'), findsWidgets);
    });
  });
}
