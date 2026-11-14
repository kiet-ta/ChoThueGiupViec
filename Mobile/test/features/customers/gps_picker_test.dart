import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/device/device_location.dart';
import 'package:mobile/core/device/location_wrapper.dart';
import 'package:mobile/core/widgets/nordic_button.dart';
import 'package:mobile/features/customers/widgets/gps_picker.dart';

/// Hands out the position the test chose, or fails the way the platform would; never touches a sensor.
class FakeLocationProvider implements IDeviceLocationProvider {
  GeoPoint? point;
  Object? error;
  Completer<GeoPoint>? gate;
  int calls = 0;

  @override
  Future<GeoPoint> currentPosition() {
    calls++;
    if (gate != null) return gate!.future;
    if (error != null) return Future.error(error!);
    return Future.value(point!);
  }
}

GeoPoint fix(double lat, double lng, {double accuracy = 12.4}) => GeoPoint(
  latitude: lat,
  longitude: lng,
  accuracyMeters: accuracy,
  timestamp: DateTime.utc(2026, 10, 21, 9),
);

/// MOB-CUSTOMER-01 (#384): "Lấy GPS Hiện Tại" reads the real position; it never reports a fixed point as the current GPS.
void main() {
  final changes = <(double, double)>[];

  Future<void> pump(WidgetTester tester, FakeLocationProvider provider) async {
    changes.clear();
    tester.view.physicalSize = const Size(390, 1200);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(() {
      tester.view.resetPhysicalSize();
      tester.view.resetDevicePixelRatio();
    });
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: SingleChildScrollView(
            child: GpsCoordinatePicker(
              latitude: 10.7769,
              longitude: 106.7009,
              locationProvider: provider,
              onCoordinatesChanged: (lat, lng) => changes.add((lat, lng)),
            ),
          ),
        ),
      ),
    );
  }

  Future<void> tapCurrent(WidgetTester tester) async {
    await tester.tap(find.text('Dùng vị trí hiện tại'));
    await tester.pump();
    await tester.pump();
  }

  testWidgets(
    'success fills the real coordinates and shows the real accuracy',
    (tester) async {
      final provider = FakeLocationProvider()
        ..point = fix(21.0285, 105.8542, accuracy: 12.4);
      await pump(tester, provider);

      await tapCurrent(tester);

      expect(changes, [(21.0285, 105.8542)]);
      expect(find.textContaining('21.02850, 105.85420'), findsOneWidget);
      expect(find.textContaining('sai số khoảng 12 m'), findsOneWidget);
      expect(
        find.textContaining('Đã cập nhật toạ độ GPS hiện tại (10.7769'),
        findsNothing,
        reason: 'the old fixed point is not reported',
      );
    },
  );

  testWidgets(
    'a different real position gives different coordinates (nothing is fixed)',
    (tester) async {
      final provider = FakeLocationProvider()
        ..point = fix(16.0544, 108.2022, accuracy: 30);
      await pump(tester, provider);

      await tapCurrent(tester);

      expect(changes, [(16.0544, 108.2022)]);
      expect(find.textContaining('sai số khoảng 30 m'), findsOneWidget);
    },
  );

  for (final c in [
    (DeviceLocationFailure.permissionDenied, 'chưa được phép dùng vị trí'),
    (DeviceLocationFailure.permissionDeniedForever, 'Cài đặt của máy'),
    (DeviceLocationFailure.serviceDisabled, 'GPS) của máy đang tắt'),
    (DeviceLocationFailure.timeout, 'hết thời gian chờ'),
    (DeviceLocationFailure.failed, 'Không lấy được vị trí hiện tại'),
  ]) {
    testWidgets(
      '${c.$1.name}: a clear Vietnamese message and the coordinates stay unchanged',
      (tester) async {
        final provider = FakeLocationProvider()
          ..error = DeviceLocationException(c.$1);
        await pump(tester, provider);

        await tapCurrent(tester);

        expect(find.textContaining(c.$2), findsOneWidget);
        expect(changes, isEmpty);
        expect(
          find.text('Dùng vị trí hiện tại'),
          findsOneWidget,
          reason: 'the button is usable again',
        );
      },
    );
  }

  testWidgets(
    'an unexpected error is the generic message and changes nothing',
    (tester) async {
      final provider = FakeLocationProvider()..error = StateError('boom');
      await pump(tester, provider);

      await tapCurrent(tester);

      expect(
        find.textContaining('Không lấy được vị trí hiện tại'),
        findsOneWidget,
      );
      expect(changes, isEmpty);
    },
  );

  testWidgets('a second tap while the position is being read asks once', (
    tester,
  ) async {
    final provider = FakeLocationProvider()..gate = Completer<GeoPoint>();
    await pump(tester, provider);

    await tapCurrent(tester);
    await tester.tap(find.byType(NordicButton).first, warnIfMissed: false);
    await tester.pump();
    expect(provider.calls, 1);

    provider.gate!.complete(fix(10.0, 106.0));
    await tester.pump();
    await tester.pump();
    expect(changes, [(10.0, 106.0)]);
  });

  testWidgets('the sample presets still set their coordinates', (tester) async {
    await pump(tester, FakeLocationProvider());

    await tester.tap(find.text('Hoàn Kiếm, Hà Nội'));
    await tester.pump();

    expect(changes, [(21.0285, 105.8542)]);
  });

  test('the real provider without a platform answers with a failure, it does not crash', () async {
    // No plugin in a unit test: the platform call fails and must come out as a DeviceLocationException, not as a raw error.
    await expectLater(
      const GeolocatorLocationProvider().currentPosition(),
      throwsA(
        isA<DeviceLocationException>().having(
          (e) => e.failure,
          'failure',
          DeviceLocationFailure.failed,
        ),
      ),
    );
  });
}
