import 'package:flutter/foundation.dart';

/// The address given when the app is built or run: `--dart-define=API_BASE_URL=http://192.168.1.10:5004`. Empty = not given.
const String apiBaseUrlOverride = String.fromEnvironment('API_BASE_URL');

/// The Android emulator's alias of the computer it runs on.
const String androidEmulatorApiBaseUrl = 'http://10.0.2.2:5004';

/// The backend on the same computer: Web (Chrome, Edge), Windows, macOS, Linux and the iOS simulator (MOB-PLATFORM-01, #942).
const String localApiBaseUrl = 'http://localhost:5004';

/// The default address for a platform when no `API_BASE_URL` was given: Android uses the emulator alias, every other
/// platform the backend on the same computer. A real phone on a USB cable uses `http://localhost:5004` (after
/// `adb reverse tcp:5004 tcp:5004`) through the define; on the same Wi-Fi it uses the computer's IP address.
String defaultApiBaseUrl({
  required bool isWeb,
  required TargetPlatform platform,
}) => !isWeb && platform == TargetPlatform.android
    ? androidEmulatorApiBaseUrl
    : localApiBaseUrl;

/// The address of the backend: `API_BASE_URL` when given, otherwise [defaultApiBaseUrl] for the running platform.
String get apiBaseUrl => apiBaseUrlOverride.isNotEmpty
    ? apiBaseUrlOverride
    : defaultApiBaseUrl(isWeb: kIsWeb, platform: defaultTargetPlatform);
