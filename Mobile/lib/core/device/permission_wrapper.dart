/// Abstract contract for runtime device permission management.
abstract class IPermissionWrapper {
  /// Checks if location permission is granted.
  Future<bool> hasLocationPermission();

  /// Prompts user to grant location permission.
  Future<bool> requestLocationPermission();

  /// Checks if camera permission is granted.
  Future<bool> hasCameraPermission();

  /// Prompts user to grant camera permission.
  Future<bool> requestCameraPermission();
}

/// PermissionWrapper implementation with mock capability for automated testing.
class PermissionWrapper implements IPermissionWrapper {
  bool _mockLocationGranted;
  bool _mockCameraGranted;

  PermissionWrapper({
    bool mockLocationGranted = true,
    bool mockCameraGranted = true,
  })  : _mockLocationGranted = mockLocationGranted,
        _mockCameraGranted = mockCameraGranted;

  void setMockLocationPermission(bool granted) {
    _mockLocationGranted = granted;
  }

  void setMockCameraPermission(bool granted) {
    _mockCameraGranted = granted;
  }

  @override
  Future<bool> hasLocationPermission() async => _mockLocationGranted;

  @override
  Future<bool> requestLocationPermission() async => _mockLocationGranted;

  @override
  Future<bool> hasCameraPermission() async => _mockCameraGranted;

  @override
  Future<bool> requestCameraPermission() async => _mockCameraGranted;
}
