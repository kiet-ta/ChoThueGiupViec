import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/device/device_location.dart';
import 'package:mobile/core/device/location_wrapper.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/core/widgets/nordic_button.dart';
import 'package:mobile/features/customers/logic/map_address.dart';
import 'package:mobile/features/customers/models/reverse_geocode_result.dart';
import 'package:mobile/features/customers/screens/address_map_screen.dart';

/// Hands out the position the test chose, or fails the way the platform would; never touches a sensor.
class FakeLocation implements IDeviceLocationProvider {
  GeoPoint? point;
  Object? error;
  int calls = 0;

  @override
  Future<GeoPoint> currentPosition() {
    calls++;
    if (error != null) return Future.error(error!);
    if (point == null) {
      return Future.error(
        const DeviceLocationException(DeviceLocationFailure.timeout),
      );
    }
    return Future.value(point!);
  }
}

GeoPoint at(double lat, double lng) => GeoPoint(
  latitude: lat,
  longitude: lng,
  accuracyMeters: 8,
  timestamp: DateTime.utc(2026, 11, 14, 9),
);

ReverseGeocodeResult place(String ward, double lat, double lng) =>
    ReverseGeocodeResult(
      city: 'Thành phố Hồ Chí Minh',
      district: ward,
      addressLine: 'Đường $ward',
      formattedAddress: 'Đường $ward, $ward, Thành phố Hồ Chí Minh',
      latitude: lat,
      longitude: lng,
    );

/// ADDR-MAP-01: "Chọn vị trí nhà" puts the pin where the customer taps and shows the address read there.
void main() {
  late FakeLocation location;
  late List<(double, double)> reads;
  late Future<ReverseGeocodeResult> Function(double, double) geocode;
  PickedPlace? picked;
  var popped = false;

  setUp(() {
    location = FakeLocation();
    reads = [];
    geocode = (lat, lng) async => place('Bến Thành', lat, lng);
    picked = null;
    popped = false;
  });

  Future<void> open(WidgetTester tester, {double? lat, double? lng}) async {
    tester.view.physicalSize = const Size(390, 844);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(() {
      tester.view.resetPhysicalSize();
      tester.view.resetDevicePixelRatio();
    });
    await tester.pumpWidget(
      MaterialApp(
        theme: NordicTheme.lightTheme,
        home: Builder(
          builder: (context) => Scaffold(
            body: Center(
              child: TextButton(
                onPressed: () async {
                  picked = await Navigator.of(context).push<PickedPlace>(
                    MaterialPageRoute(
                      builder: (_) => AddressMapScreen(
                        initialLatitude: lat,
                        initialLongitude: lng,
                        reverseGeocode: (la, ln) {
                          reads.add((la, ln));
                          return geocode(la, ln);
                        },
                        locationProvider: location,
                        showTiles: false,
                      ),
                    ),
                  );
                  popped = true;
                },
                child: const Text('open'),
              ),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();
  }

  /// Taps the map in its upper part (the panel covers the bottom), past the double-tap wait.
  Future<void> tapMap(
    WidgetTester tester, {
    double dx = 0,
    double dy = 0,
  }) async {
    final topLeft = tester.getTopLeft(find.byKey(const Key('address-map')));
    await tester.tapAt(topLeft + Offset(195 + dx, 200 + dy));
    await tester.pump(const Duration(milliseconds: 500));
  }

  NordicButton confirm(WidgetTester tester) =>
      tester.widget<NordicButton>(find.byKey(const Key('address-map-confirm')));

  testWidgets(
    'a new address: the map asks for a tap and "Dùng vị trí này" waits for it',
    (tester) async {
      await open(tester);
      expect(find.text('Chọn vị trí nhà'), findsOneWidget);
      expect(find.text('Chạm vào nhà bạn trên bản đồ'), findsOneWidget);
      expect(find.byKey(const Key('address-map-pin')), findsNothing);
      expect(confirm(tester).onPressed, isNull);
      // The map tries to start where the phone is (no pin: the customer may not be at home).
      expect(location.calls, 1);
    },
  );

  testWidgets(
    'a tap puts the pin there, reads the address and "Dùng vị trí này" returns both',
    (tester) async {
      await open(tester);
      await tapMap(tester);
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('address-map-pin')), findsOneWidget);
      expect(reads, hasLength(1));
      expect(find.text('Địa chỉ ở điểm này'), findsOneWidget);
      expect(
        find.text('Đường Bến Thành, Bến Thành, Thành phố Hồ Chí Minh'),
        findsOneWidget,
      );

      await tester.tap(find.byKey(const Key('address-map-confirm')));
      await tester.pumpAndSettle();
      expect(popped, isTrue);
      expect(picked!.latitude, reads.single.$1);
      expect(picked!.longitude, reads.single.$2);
      expect(picked!.address!.district, 'Bến Thành');
    },
  );

  testWidgets(
    'while the address is read the panel says so and the button waits',
    (tester) async {
      final gate = Completer<ReverseGeocodeResult>();
      geocode = (lat, lng) => gate.future;
      await open(tester);
      await tapMap(tester);
      expect(find.text('Đang tìm địa chỉ…'), findsOneWidget);
      expect(confirm(tester).onPressed, isNull);
      gate.complete(place('Sài Gòn', 10.77, 106.70));
      await tester.pumpAndSettle();
      expect(confirm(tester).onPressed, isNotNull);
    },
  );

  testWidgets(
    'an address that cannot be read keeps the point and says the customer can type it',
    (tester) async {
      geocode = (lat, lng) => Future.error(Exception('down'));
      await open(tester);
      await tapMap(tester);
      await tester.pumpAndSettle();
      expect(
        find.text('Không đọc được địa chỉ ở điểm này. Bạn có thể nhập tay.'),
        findsOneWidget,
      );

      await tester.tap(find.byKey(const Key('address-map-confirm')));
      await tester.pumpAndSettle();
      expect(picked, isNotNull);
      expect(picked!.address, isNull);
    },
  );

  testWidgets(
    'a late answer for an earlier tap never replaces the answer for the last tap',
    (tester) async {
      final first = Completer<ReverseGeocodeResult>();
      final second = Completer<ReverseGeocodeResult>();
      final gates = [first, second];
      geocode = (lat, lng) => gates.removeAt(0).future;
      await open(tester);
      await tapMap(tester, dx: -60);
      await tapMap(tester, dx: 60);
      second.complete(place('Sài Gòn', 10.77, 106.70));
      await tester.pumpAndSettle();
      first.complete(place('Bến Thành', 10.77, 106.69));
      await tester.pumpAndSettle();

      expect(find.textContaining('Sài Gòn'), findsOneWidget);
      expect(find.textContaining('Bến Thành'), findsNothing);
      await tester.tap(find.byKey(const Key('address-map-confirm')));
      await tester.pumpAndSettle();
      expect(picked!.address!.district, 'Sài Gòn');
      expect(picked!.longitude, reads.last.$2);
    },
  );

  testWidgets(
    '"Vị trí của tôi" puts the pin at the phone and reads that address',
    (tester) async {
      await open(tester);
      location.point = at(10.8012, 106.7145);
      await tester.tap(find.byKey(const Key('address-map-locate')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('address-map-pin')), findsOneWidget);
      expect(reads.single, (10.8012, 106.7145));
    },
  );

  testWidgets(
    '"Vị trí của tôi" explains a refused permission and keeps the map usable',
    (tester) async {
      location.error = const DeviceLocationException(
        DeviceLocationFailure.permissionDenied,
      );
      await open(tester);
      await tester.tap(find.byKey(const Key('address-map-locate')));
      await tester.pumpAndSettle();
      expect(
        find.text(
          deviceLocationFailureText(DeviceLocationFailure.permissionDenied),
        ),
        findsOneWidget,
      );
      expect(find.byKey(const Key('address-map-pin')), findsNothing);
    },
  );

  testWidgets(
    'a saved address opens on its pin without asking the phone, and can be kept as it is',
    (tester) async {
      await open(tester, lat: 10.7724, lng: 106.698);
      expect(location.calls, 0);
      expect(find.byKey(const Key('address-map-pin')), findsOneWidget);
      expect(find.text('Vị trí đã lưu'), findsOneWidget);
      await tester.tap(find.byKey(const Key('address-map-confirm')));
      await tester.pumpAndSettle();
      expect(picked!.latitude, 10.7724);
      expect(picked!.address, isNull);
      expect(reads, isEmpty);
    },
  );
}
