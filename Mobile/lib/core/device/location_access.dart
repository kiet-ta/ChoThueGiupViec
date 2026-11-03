import 'package:geolocator/geolocator.dart';

/// Whether the app may read the phone's position right now (no position is read here).
enum LocationAccessState {
  /// The location switch is on and the permission is granted: the position can be read.
  granted,

  /// The location switch of the phone is off.
  serviceDisabled,

  /// The permission is not granted (never asked, or refused): it can be asked for.
  permissionDenied,

  /// The permission was refused for good: only the app's settings in the phone can change it.
  permissionDeniedForever,
}

/// What the Worker gate needs of the phone: the state of the location switch and permission, asking for it, and the settings pages.
abstract class ILocationAccess {
  /// The state now, without asking the person anything.
  Future<LocationAccessState> check();

  /// Asks for the permission (a system dialog when it can be asked) and returns the state afterwards.
  Future<LocationAccessState> requestPermission();

  /// Opens the phone's location settings (to switch location on).
  Future<void> openLocationSettings();

  /// Opens the app's page in the phone's settings (to grant a permission refused for good).
  Future<void> openAppSettings();
}

/// `geolocator` behind [ILocationAccess].
class GeolocatorLocationAccess implements ILocationAccess {
  const GeolocatorLocationAccess();

  @override
  Future<LocationAccessState> check() async {
    if (!await Geolocator.isLocationServiceEnabled()) {
      return LocationAccessState.serviceDisabled;
    }
    return _fromPermission(await Geolocator.checkPermission());
  }

  @override
  Future<LocationAccessState> requestPermission() async {
    if (!await Geolocator.isLocationServiceEnabled()) {
      return LocationAccessState.serviceDisabled;
    }
    var permission = await Geolocator.checkPermission();
    if (permission == LocationPermission.denied) {
      permission = await Geolocator.requestPermission();
    }
    return _fromPermission(permission);
  }

  @override
  Future<void> openLocationSettings() async {
    await Geolocator.openLocationSettings();
  }

  @override
  Future<void> openAppSettings() async {
    await Geolocator.openAppSettings();
  }

  static LocationAccessState _fromPermission(LocationPermission permission) =>
      switch (permission) {
        LocationPermission.always ||
        LocationPermission.whileInUse => LocationAccessState.granted,
        LocationPermission.deniedForever =>
          LocationAccessState.permissionDeniedForever,
        LocationPermission.denied || LocationPermission.unableToDetermine =>
          LocationAccessState.permissionDenied,
      };
}
