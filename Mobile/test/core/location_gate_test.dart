import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/device/location_access.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/core/widgets/location_gate.dart';
import 'package:mobile/features/home/home_screen.dart';
import 'package:mobile/features/identity/models/auth_models.dart';
import 'package:mobile/features/identity/services/token_storage.dart';

import '../features/workers/design_frame.dart';

// MOB-WORKER-GPS-01 (#631): the Worker app cannot be used without location.

/// A phone whose state the test sets; records what the gate asked of it.
class _Phone implements ILocationAccess {
  _Phone(this.state);

  LocationAccessState state;

  /// What the phone becomes after the person grants the permission in the system dialog.
  LocationAccessState afterRequest = LocationAccessState.granted;
  Object? failure;
  int checks = 0;
  int requests = 0;
  int locationSettings = 0;
  int appSettings = 0;

  @override
  Future<LocationAccessState> check() async {
    checks++;
    if (failure != null) throw failure!;
    return state;
  }

  @override
  Future<LocationAccessState> requestPermission() async {
    requests++;
    state = afterRequest;
    return state;
  }

  @override
  Future<void> openLocationSettings() async => locationSettings++;

  @override
  Future<void> openAppSettings() async => appSettings++;
}

const _shell = Text('the worker shell');
const _button = Key('location_gate_button');

Widget _gate(_Phone phone) => MaterialApp(
  theme: NordicTheme.lightTheme,
  home: LocationGate(access: phone, child: _shell),
);

void main() {
  group('LocationGate', () {
    testWidgets('location on and permitted: the shell is shown', (
      tester,
    ) async {
      useDesignFrame(tester);
      await tester.pumpWidget(_gate(_Phone(LocationAccessState.granted)));
      await tester.pumpAndSettle();

      expect(find.text('the worker shell'), findsOneWidget);
      expect(find.byKey(_button), findsNothing);
    });

    testWidgets(
      'location switch off: the shell is hidden and the button opens the location settings',
      (tester) async {
        useDesignFrame(tester);
        final phone = _Phone(LocationAccessState.serviceDisabled);
        await tester.pumpWidget(_gate(phone));
        await tester.pumpAndSettle();

        expect(find.text('the worker shell'), findsNothing);
        expect(find.text('Hãy bật định vị'), findsOneWidget);
        expect(find.text('Bật định vị'), findsOneWidget);

        await tester.tap(find.byKey(_button));
        await tester.pumpAndSettle();

        expect(phone.locationSettings, 1);
        expect(find.text('the worker shell'), findsNothing);
      },
    );

    testWidgets(
      'permission not granted: the button asks, and the shell opens once it is granted',
      (tester) async {
        useDesignFrame(tester);
        final phone = _Phone(LocationAccessState.permissionDenied);
        await tester.pumpWidget(_gate(phone));
        await tester.pumpAndSettle();

        expect(find.text('Cần quyền vị trí'), findsOneWidget);
        expect(find.text('the worker shell'), findsNothing);

        await tester.tap(find.byKey(_button));
        await tester.pumpAndSettle();

        expect(phone.requests, 1);
        expect(find.text('the worker shell'), findsOneWidget);
      },
    );

    testWidgets(
      'permission refused again: still no shell, and the button can ask again',
      (tester) async {
        useDesignFrame(tester);
        final phone = _Phone(LocationAccessState.permissionDenied)
          ..afterRequest = LocationAccessState.permissionDenied;
        await tester.pumpWidget(_gate(phone));
        await tester.pumpAndSettle();

        await tester.tap(find.byKey(_button));
        await tester.pumpAndSettle();
        await tester.tap(find.byKey(_button));
        await tester.pumpAndSettle();

        expect(phone.requests, 2);
        expect(find.text('the worker shell'), findsNothing);
      },
    );

    testWidgets('refused for good: the button opens the app settings', (
      tester,
    ) async {
      useDesignFrame(tester);
      final phone = _Phone(LocationAccessState.permissionDeniedForever);
      await tester.pumpWidget(_gate(phone));
      await tester.pumpAndSettle();

      expect(find.text('Quyền vị trí đang bị chặn'), findsOneWidget);
      expect(find.text('Mở cài đặt'), findsOneWidget);

      await tester.tap(find.byKey(_button));
      await tester.pumpAndSettle();

      expect(phone.appSettings, 1);
      expect(phone.requests, 0);
      expect(find.text('the worker shell'), findsNothing);
    });

    testWidgets(
      'coming back to the app checks again: a worker who switched location on gets in, one who switched it off is stopped',
      (tester) async {
        useDesignFrame(tester);
        final phone = _Phone(LocationAccessState.serviceDisabled);
        await tester.pumpWidget(_gate(phone));
        await tester.pumpAndSettle();
        expect(find.text('the worker shell'), findsNothing);

        phone.state = LocationAccessState.granted;
        tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.paused);
        tester.binding.handleAppLifecycleStateChanged(
          AppLifecycleState.resumed,
        );
        await tester.pumpAndSettle();
        expect(find.text('the worker shell'), findsOneWidget);

        phone.state = LocationAccessState.serviceDisabled;
        tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.paused);
        tester.binding.handleAppLifecycleStateChanged(
          AppLifecycleState.resumed,
        );
        await tester.pumpAndSettle();
        expect(find.text('the worker shell'), findsNothing);
        expect(find.text('Hãy bật định vị'), findsOneWidget);
      },
    );

    testWidgets(
      'a check that fails never lets the worker in and can be retried',
      (tester) async {
        useDesignFrame(tester);
        final phone = _Phone(LocationAccessState.granted)
          ..failure = StateError('no plugin');
        await tester.pumpWidget(_gate(phone));
        await tester.pumpAndSettle();

        expect(find.text('the worker shell'), findsNothing);
        expect(
          find.text('Chưa kiểm tra được quyền vị trí của điện thoại.'),
          findsOneWidget,
        );

        phone.failure = null;
        await tester.tap(find.text('Thử lại'));
        await tester.pumpAndSettle();

        expect(find.text('the worker shell'), findsOneWidget);
      },
    );
  });

  group('the gate on the Home', () {
    AuthResult session(String role) => AuthResult(
      tokenType: 'Bearer',
      accessToken: 'token',
      accessTokenExpiresInSeconds: 900,
      refreshToken: 'refresh',
      user: AuthUser(id: 12, role: role, isNewUser: false),
    );

    setUp(() => TokenStorage().clear());
    tearDown(() => TokenStorage().clear());

    testWidgets(
      'a signed-in Worker without location sees the gate, not the shell',
      (tester) async {
        useDesignFrame(tester);
        TokenStorage().saveAuth(session('Worker'));
        final phone = _Phone(LocationAccessState.serviceDisabled);

        await tester.pumpWidget(
          MaterialApp(home: HomeScreen(locationAccess: phone)),
        );
        await tester.pumpAndSettle();

        expect(find.text('Hãy bật định vị'), findsOneWidget);
        expect(find.text('Thợ #12'), findsNothing);
        expect(phone.checks, greaterThanOrEqualTo(1));
      },
    );

    testWidgets(
      'a signed-in Customer is not gated: the phone is not even asked',
      (tester) async {
        useDesignFrame(tester);
        TokenStorage().saveAuth(session('Customer'));
        final phone = _Phone(LocationAccessState.serviceDisabled);

        await tester.pumpWidget(
          MaterialApp(home: HomeScreen(locationAccess: phone)),
        );
        await tester.pumpAndSettle();

        expect(find.text('Hãy bật định vị'), findsNothing);
        expect(phone.checks, 0);
      },
    );
  });
}
