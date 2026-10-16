import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/home/home_screen.dart';
import 'package:mobile/features/identity/models/auth_models.dart';
import 'package:mobile/features/identity/services/token_storage.dart';

import '../workers/design_frame.dart';

AuthResult _session(String role) => AuthResult(
  tokenType: 'Bearer',
  accessToken: 'token',
  accessTokenExpiresInSeconds: 900,
  refreshToken: 'refresh',
  user: AuthUser(id: 12, role: role, isNewUser: false),
);

/// The Home inside an app whose named routes only record that they were opened.
Widget _app(List<String> opened) => MaterialApp(
  home: const HomeScreen(),
  onGenerateRoute: (settings) {
    opened.add(settings.name ?? '');
    return MaterialPageRoute(
      builder: (_) => Scaffold(body: Text('opened ${settings.name}')),
      settings: settings,
    );
  },
);

void main() {
  setUp(() => TokenStorage().clear());
  tearDown(() => TokenStorage().clear());

  testWidgets('signed out: no session panel, Dat Ca Ngay asks to sign in', (
    tester,
  ) async {
    useDesignFrame(tester);
    final opened = <String>[];
    await tester.pumpWidget(_app(opened));

    expect(find.byKey(const Key('home-sign-out')), findsNothing);

    await tapVisible(tester, find.text('Đặt Ca Ngay'));

    expect(find.text('Đăng nhập bằng số điện thoại'), findsOneWidget);
    expect(opened, isEmpty);
  });

  testWidgets(
    'a signed-in customer sees the actions and Dat Ca Ngay opens booking',
    (tester) async {
      TokenStorage().saveAuth(_session('Customer'));
      useDesignFrame(tester);
      final opened = <String>[];
      await tester.pumpWidget(_app(opened));

      expect(find.text('Khách hàng #12'), findsOneWidget);
      expect(find.text('Đặt ca'), findsOneWidget);
      expect(find.text('Đơn của tôi'), findsOneWidget);

      await tapVisible(tester, find.text('Đặt Ca Ngay'));

      expect(opened, ['/booking']);
    },
  );

  testWidgets('a customer action opens its route', (tester) async {
    TokenStorage().saveAuth(_session('Customer'));
    useDesignFrame(tester);
    final opened = <String>[];
    await tester.pumpWidget(_app(opened));

    await tapVisible(
      tester,
      find.byKey(const Key('home-action-/booking/orders')),
    );

    expect(opened, ['/booking/orders']);
  });

  testWidgets(
    'a signed-in worker sees the worker actions and Vao Truc Ca opens the slots',
    (tester) async {
      TokenStorage().saveAuth(_session('Worker'));
      useDesignFrame(tester);
      final opened = <String>[];
      await tester.pumpWidget(_app(opened));

      expect(find.text('Thợ #12'), findsOneWidget);
      expect(find.text('Lịch rảnh'), findsOneWidget);
      expect(find.text('Nhận việc'), findsOneWidget);

      await tapVisible(tester, find.text('Vào Trực Ca'));

      expect(opened, ['/workers/slots']);
    },
  );

  testWidgets('signing out clears the session and the panel', (tester) async {
    TokenStorage().saveAuth(_session('Customer'));
    useDesignFrame(tester);
    await tester.pumpWidget(_app(<String>[]));

    await tapVisible(tester, find.byKey(const Key('home-sign-out')));

    expect(TokenStorage().isAuthenticated, isFalse);
    expect(find.byKey(const Key('home-sign-out')), findsNothing);
  });
}
