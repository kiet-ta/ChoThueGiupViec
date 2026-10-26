import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/home/customer_shell.dart';
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

  testWidgets('signed out: the minimal welcome, no landing page', (
    tester,
  ) async {
    useDesignFrame(tester);
    await tester.pumpWidget(_app(<String>[]));

    expect(find.text('TỔ ẤM'), findsOneWidget);
    expect(find.text('Tôi cần giúp việc'), findsOneWidget);
    expect(find.text('Tôi là người giúp việc'), findsOneWidget);
    expect(find.text('Bảo hiểm an tâm 50 triệu'), findsNothing);
    expect(find.text('240.000 đ'), findsNothing);
    expect(find.byType(CustomerShell), findsNothing);
  });

  testWidgets('signed out: the customer button opens the phone login', (
    tester,
  ) async {
    useDesignFrame(tester);
    final opened = <String>[];
    await tester.pumpWidget(_app(opened));

    await tapVisible(tester, find.byKey(const Key('welcome-customer')));

    expect(find.byKey(const Key('phone-input-title')), findsOneWidget);
    expect(opened, isEmpty);
  });

  testWidgets('a signed-in customer lands on the shell with the bottom bar', (
    tester,
  ) async {
    TokenStorage().saveAuth(_session('Customer'));
    useDesignFrame(tester);
    await tester.pumpWidget(_app(<String>[]));
    await tester.pumpAndSettle();

    expect(find.byType(CustomerShell), findsOneWidget);
    for (final label in ['Trang chủ', 'Đơn', 'Đặt ca', 'Yêu thích']) {
      expect(find.text(label), findsWidgets);
    }
  });

  testWidgets('the centre button of the customer shell opens booking', (
    tester,
  ) async {
    TokenStorage().saveAuth(_session('Customer'));
    useDesignFrame(tester);
    final opened = <String>[];
    await tester.pumpWidget(_app(opened));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('customer-shell-book')));
    await tester.pumpAndSettle();

    expect(opened, ['/booking']);
  });

  testWidgets('signing out from the account tab shows the welcome again', (
    tester,
  ) async {
    TokenStorage().saveAuth(_session('Customer'));
    useDesignFrame(tester);
    await tester.pumpWidget(_app(<String>[]));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('customer-tab-3')));
    await tester.pumpAndSettle();
    await tapVisible(tester, find.byKey(const Key('home-sign-out')));

    expect(TokenStorage().isAuthenticated, isFalse);
    expect(find.text('Tôi cần giúp việc'), findsOneWidget);
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

  testWidgets('a worker signs out from the panel', (tester) async {
    TokenStorage().saveAuth(_session('Worker'));
    useDesignFrame(tester);
    await tester.pumpWidget(_app(<String>[]));

    await tapVisible(tester, find.byKey(const Key('home-sign-out')));

    expect(TokenStorage().isAuthenticated, isFalse);
    expect(find.text('Tôi cần giúp việc'), findsOneWidget);
  });
}
