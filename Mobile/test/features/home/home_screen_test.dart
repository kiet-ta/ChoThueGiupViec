import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/home/customer_shell.dart';
import 'package:mobile/features/home/home_screen.dart';
import 'package:mobile/features/home/worker_shell.dart';
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

  testWidgets('signed out: each role card says what the role does', (
    tester,
  ) async {
    useDesignFrame(tester);
    await tester.pumpWidget(_app(<String>[]));

    expect(find.text('BẠN LÀ AI?'), findsOneWidget);
    expect(
      find.descendant(
        of: find.byKey(const Key('welcome-customer')),
        matching: find.text(
          'Đặt ca dọn nhà, theo dõi đơn và giữ người giúp việc yêu thích.',
        ),
      ),
      findsOneWidget,
    );
    expect(
      find.descendant(
        of: find.byKey(const Key('welcome-worker')),
        matching: find.text(
          'Nhận việc mới, sắp lịch rảnh và theo dõi thu nhập.',
        ),
      ),
      findsOneWidget,
    );
    expect(find.text('Đăng nhập bằng số điện thoại và mã OTP'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('signed out: the worker card opens the phone login', (
    tester,
  ) async {
    useDesignFrame(tester);
    final opened = <String>[];
    await tester.pumpWidget(_app(opened));

    await tapVisible(tester, find.byKey(const Key('welcome-worker')));

    expect(find.byKey(const Key('phone-input-title')), findsOneWidget);
    expect(opened, isEmpty);
  });

  testWidgets('signed out: the welcome scrolls on a short screen', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(360, 560);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.reset);
    await tester.pumpWidget(_app(<String>[]));

    expect(tester.takeException(), isNull);
    await tapVisible(tester, find.byKey(const Key('welcome-worker')));
    expect(find.byKey(const Key('phone-input-title')), findsOneWidget);
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

  testWidgets('a signed-in worker lands on the shell with the bottom bar', (
    tester,
  ) async {
    TokenStorage().saveAuth(_session('Worker'));
    useDesignFrame(tester);
    await tester.pumpWidget(_app(<String>[]));

    expect(find.byType(WorkerShell), findsOneWidget);
    expect(find.text('Thợ #12'), findsOneWidget);
    for (final label in ['Hôm nay', 'Lịch', 'Việc', 'Thu nhập', 'Tài khoản']) {
      expect(find.text(label), findsWidgets);
    }
  });

  testWidgets('the centre button of the worker shell opens the offers', (
    tester,
  ) async {
    TokenStorage().saveAuth(_session('Worker'));
    useDesignFrame(tester);
    final opened = <String>[];
    await tester.pumpWidget(_app(opened));

    await tester.tap(find.byKey(const Key('worker-shell-job')));
    await tester.pumpAndSettle();

    expect(opened, ['/dispatch/offers']);
  });

  testWidgets('a worker signs out from the account tab', (tester) async {
    TokenStorage().saveAuth(_session('Worker'));
    useDesignFrame(tester);
    await tester.pumpWidget(_app(<String>[]));

    await tester.tap(find.byKey(const Key('worker-tab-3')));
    await tester.pumpAndSettle();
    await tapVisible(tester, find.byKey(const Key('home-sign-out')));

    expect(TokenStorage().isAuthenticated, isFalse);
    expect(find.text('Tôi cần giúp việc'), findsOneWidget);
  });
}
