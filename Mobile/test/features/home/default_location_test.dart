import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/device/device_location.dart';
import 'package:mobile/core/device/location_wrapper.dart';

/// Answers like the phone: a position, or a failure.
class _Provider implements IDeviceLocationProvider {
  _Provider({this.point, this.failure});

  final GeoPoint? point;
  final DeviceLocationFailure? failure;
  int reads = 0;

  @override
  Future<GeoPoint> currentPosition() async {
    reads++;
    if (failure != null) throw DeviceLocationException(failure!);
    return point!;
  }
}

void main() {
  test(
    'the default position is the one the phone reports (MOB-WORKER-GPS-01)',
    () async {
      final phone = GeoPoint(
        latitude: 10.8231,
        longitude: 106.6297,
        accuracyMeters: 12.0,
        timestamp: DateTime.utc(2026, 11, 3),
      );
      final provider = _Provider(point: phone);

      final point = await LocationWrapper(provider: provider)
          .getCurrentLocation();

      expect(point.latitude, 10.8231);
      expect(point.longitude, 106.6297);
      expect(point.accuracyMeters, 12.0);
      expect(provider.reads, 1);
    },
  );

  test(
    'a position that cannot be read is a failure, never a sample position',
    () async {
      for (final failure in DeviceLocationFailure.values) {
        final wrapper = LocationWrapper(provider: _Provider(failure: failure));

        await expectLater(
          wrapper.getCurrentLocation(),
          throwsA(
            isA<DeviceLocationException>().having(
              (e) => e.failure,
              'failure',
              failure,
            ),
          ),
        );
      }
    },
  );

  test(
    'a mock location (tests only) is returned without asking the phone',
    () async {
      final provider = _Provider(failure: DeviceLocationFailure.failed);
      final mock = GeoPoint(
        latitude: 1.0,
        longitude: 2.0,
        timestamp: DateTime.utc(2026, 11, 3),
      );

      final point = await LocationWrapper(
        mockLocation: mock,
        provider: provider,
      ).getCurrentLocation();

      expect(point.latitude, 1.0);
      expect(provider.reads, 0);
    },
  );

  test('every failure has its own words for the screen', () {
    final texts = DeviceLocationFailure.values
        .map(deviceLocationFailureText)
        .toSet();

    expect(texts.length, DeviceLocationFailure.values.length);
    expect(texts.every((t) => t.isNotEmpty), isTrue);
  });

  test('a worker at a point is within the check-in radius of that point', () {
    final wrapper = LocationWrapper(
      provider: _Provider(failure: DeviceLocationFailure.failed),
    );

    expect(
      wrapper.isWithinRadius(10.7769, 106.7009, 10.7769, 106.7009, 100.0),
      isTrue,
    );
  });
}
