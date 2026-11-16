# mobile

A new Flutter project.

## Getting Started

This project is a starting point for a Flutter application.

A few resources to get you started if this is your first Flutter project:

- [Learn Flutter](https://docs.flutter.dev/get-started/learn-flutter)
- [Write your first Flutter app](https://docs.flutter.dev/get-started/codelab)
- [Flutter learning resources](https://docs.flutter.dev/reference/learning-resources)

For help getting started with Flutter development, view the
[online documentation](https://docs.flutter.dev/), which offers tutorials,
samples, guidance on mobile development, and a full API reference.

## Run against the backend

The backend address is the compile-time constant `API_BASE_URL` (`lib/core/network/api_config.dart`). Without it the app uses `http://10.0.2.2:5004` on Android (the **emulator**'s alias of the computer) and `http://localhost:5004` on every other platform: a browser, Windows, macOS, Linux, the iOS simulator (MOB-PLATFORM-01, decisions D39).

Start the backend in Development (`cd Backend && dotnet run --no-launch-profile` with `ASPNETCORE_ENVIRONMENT=Development` and a connection string), then:

| Where the app runs | How |
|---|---|
| Android emulator | `flutter run` |
| Real phone, USB cable, **no network needed** | enable USB debugging, plug the phone in, `adb reverse tcp:5004 tcp:5004`, then `flutter run --dart-define=API_BASE_URL=http://localhost:5004`. Run `adb reverse` again after every re-plug. |
| Chrome / Edge (Web) | `flutter run -d chrome` (or `-d edge`). The backend must run in **Development**: it then answers the browser's CORS check for `http://localhost:<any port>`. Staging and Production allow no origin yet. |
| Windows desktop | `flutter run -d windows`. Windows needs **Developer Mode** (`start ms-settings:developers`) to build plugins (they use symlinks). No camera on desktop: "take photo" opens the file picker. |
| macOS / Linux desktop | `flutter run -d macos` / `-d linux`. The build files are in place (macOS has the network entitlement); not checked on a Mac or a Linux machine yet. |
| Real phone, same Wi-Fi | start the backend with `ASPNETCORE_URLS=http://0.0.0.0:5004`, allow port 5004 in the firewall, then `flutter run --dart-define=API_BASE_URL=http://<computer IP>:5004` |

The backend speaks plain HTTP. A debug build (`flutter run`) and a release build (`flutter run --release`, with the `INTERNET` permission of this manifest) both reached it on an Android 17 emulator on 2026-10-21 (an OTP request over `http://10.0.2.2:5004`). Plain HTTP is not encrypted: use it only for a local demo, and use HTTPS for a real deployment.
