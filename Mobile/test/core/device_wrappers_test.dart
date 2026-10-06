import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/device/camera_wrapper.dart';
import 'package:mobile/core/device/location_wrapper.dart';
import 'package:mobile/core/device/permission_wrapper.dart';

void main() {
  group('LocationWrapper Tests', () {
    late LocationWrapper locationWrapper;

    setUp(() {
      locationWrapper = LocationWrapper();
    });

    test('calculateDistanceMeters computes 0 for identical points', () {
      final distance = locationWrapper.calculateDistanceMeters(
        21.028511,
        105.854167,
        21.028511,
        105.854167,
      );

      expect(distance, closeTo(0.0, 0.001));
    });

    test('calculateDistanceMeters computes known distance between Hanoi coordinates', () {
      // Point A: Hoan Kiem Lake (21.028511, 105.854167)
      // Point B: St. Joseph Cathedral (~450 meters away, 21.0298, 105.8495)
      final distance = locationWrapper.calculateDistanceMeters(
        21.028511,
        105.854167,
        21.0298,
        105.8495,
      );

      expect(distance, greaterThan(400.0));
      expect(distance, lessThan(600.0));
    });

    test('isWithinRadius validates GPS check-in <= 100m rule', () {
      // 50 meters away
      final within50m = locationWrapper.isWithinRadius(
        21.028511,
        105.854167,
        21.028700,
        105.854400,
        100.0,
      );
      expect(within50m, isTrue);

      // 500 meters away
      final beyond100m = locationWrapper.isWithinRadius(
        21.028511,
        105.854167,
        21.033000,
        105.854167,
        100.0,
      );
      expect(beyond100m, isFalse);
    });

    test('setMockLocation injects testable GPS coordinate', () async {
      final custom = GeoPoint(
        latitude: 10.776889,
        longitude: 106.700806,
        accuracyMeters: 2.0,
        timestamp: DateTime.utc(2026, 10, 6),
      );

      locationWrapper.setMockLocation(custom);
      final current = await locationWrapper.getCurrentLocation();

      expect(current.latitude, 10.776889);
      expect(current.longitude, 106.700806);
    });
  });

  group('CameraWrapper Tests', () {
    test('capturePhoto returns photo with 640px default VoL width', () async {
      final camera = CameraWrapper();
      final photo = await camera.capturePhoto();

      expect(photo, isNotNull);
      expect(photo!.width, 640);
      expect(photo.height, 480);
      expect(photo.path, contains('/mock/photos/'));
    });

    test('setMockImage returns injected captured image', () async {
      final camera = CameraWrapper();
      final mock = CapturedImage(
        path: '/test/before.jpg',
        capturedAt: DateTime.utc(2026, 10, 6),
      );

      camera.setMockImage(mock);
      final photo = await camera.capturePhoto();

      expect(photo?.path, '/test/before.jpg');
    });
  });

  group('PermissionWrapper Tests', () {
    test('manages location and camera permission states', () async {
      final permissions = PermissionWrapper();

      expect(await permissions.hasLocationPermission(), isTrue);
      expect(await permissions.hasCameraPermission(), isTrue);

      permissions.setMockLocationPermission(false);
      expect(await permissions.hasLocationPermission(), isFalse);

      permissions.setMockCameraPermission(false);
      expect(await permissions.hasCameraPermission(), isFalse);
    });
  });
}
