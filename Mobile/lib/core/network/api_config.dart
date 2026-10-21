/// The address of the backend, set when the app is built or run: `--dart-define=API_BASE_URL=http://localhost:5004`.
///
/// The default is the Android emulator's alias of the computer it runs on. A real phone on a USB cable uses
/// `http://localhost:5004` (after `adb reverse tcp:5004 tcp:5004`); on the same Wi-Fi it uses the computer's IP address.
const String apiBaseUrl = String.fromEnvironment(
  'API_BASE_URL',
  defaultValue: 'http://10.0.2.2:5004',
);
