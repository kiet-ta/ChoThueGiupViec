import 'dart:async';

import 'package:geolocator/geolocator.dart';

import 'location_wrapper.dart';

/// Why the device could not give its position.
enum DeviceLocationFailure {
  /// The location switch of the phone is off.
  serviceDisabled,

  /// The person refused the permission (it can be asked again).
  permissionDenied,

  /// The person refused for good: only the phone's settings can change it.
  permissionDeniedForever,

  /// No fix within the time limit.
  timeout,

  /// Anything else the platform reported.
  failed,
}

/// The words for a failure to read the position, for the screen that asked.
String deviceLocationFailureText(
  DeviceLocationFailure failure,
) => switch (failure) {
  DeviceLocationFailure.serviceDisabled =>
    'Vị trí trên điện thoại đang tắt. Hãy bật vị trí rồi thử lại.',
  DeviceLocationFailure.permissionDenied =>
    'Bạn chưa cho phép ứng dụng dùng vị trí. Hãy cho phép rồi thử lại.',
  DeviceLocationFailure.permissionDeniedForever =>
    'Ứng dụng bị chặn dùng vị trí. Hãy bật lại trong Cài đặt của điện thoại.',
  DeviceLocationFailure.timeout =>
    'Chưa lấy được vị trí. Hãy ra chỗ thoáng và thử lại.',
  DeviceLocationFailure.failed => 'Chưa lấy được vị trí. Vui lòng thử lại.',
};

class DeviceLocationException implements Exception {
  final DeviceLocationFailure failure;

  const DeviceLocationException(this.failure);

  @override
  String toString() => 'DeviceLocationException(${failure.name})';
}

/// The position of the phone, read once, in the foreground only (no background location).
abstract class IDeviceLocationProvider {
  /// The actual position with the accuracy the platform reported; throws a [DeviceLocationException] when it cannot.
  Future<GeoPoint> currentPosition();
}

/// `geolocator` behind [IDeviceLocationProvider]: asks for the permission when it is not granted yet.
class GeolocatorLocationProvider implements IDeviceLocationProvider {
  static const Duration timeLimit = Duration(seconds: 15);

  const GeolocatorLocationProvider();

  @override
  Future<GeoPoint> currentPosition() async {
    try {
      if (!await Geolocator.isLocationServiceEnabled()) {
        throw const DeviceLocationException(
          DeviceLocationFailure.serviceDisabled,
        );
      }

      var permission = await Geolocator.checkPermission();
      if (permission == LocationPermission.denied) {
        permission = await Geolocator.requestPermission();
      }
      if (permission == LocationPermission.deniedForever) {
        throw const DeviceLocationException(
          DeviceLocationFailure.permissionDeniedForever,
        );
      }
      if (permission == LocationPermission.denied ||
          permission == LocationPermission.unableToDetermine) {
        throw const DeviceLocationException(
          DeviceLocationFailure.permissionDenied,
        );
      }

      final position = await Geolocator.getCurrentPosition(
        locationSettings: const LocationSettings(
          accuracy: LocationAccuracy.high,
          timeLimit: timeLimit,
        ),
      );
      return GeoPoint(
        latitude: position.latitude,
        longitude: position.longitude,
        accuracyMeters: position.accuracy,
        timestamp: position.timestamp.toUtc(),
      );
    } on DeviceLocationException {
      rethrow;
    } on TimeoutException {
      throw const DeviceLocationException(DeviceLocationFailure.timeout);
    } on LocationServiceDisabledException {
      throw const DeviceLocationException(
        DeviceLocationFailure.serviceDisabled,
      );
    } on PermissionDeniedException {
      throw const DeviceLocationException(
        DeviceLocationFailure.permissionDenied,
      );
    } catch (_) {
      throw const DeviceLocationException(DeviceLocationFailure.failed);
    }
  }
}
