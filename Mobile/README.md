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

The backend address is the compile-time constant `API_BASE_URL` (`lib/core/network/api_config.dart`). Without it the app uses `http://10.0.2.2:5004`, the Android **emulator**'s alias of the computer.

Start the backend in Development (`cd Backend && dotnet run --no-launch-profile` with `ASPNETCORE_ENVIRONMENT=Development` and a connection string), then:

| Where the app runs | How |
|---|---|
| Android emulator | `flutter run` |
| Real phone, USB cable, **no network needed** | enable USB debugging, plug the phone in, `adb reverse tcp:5004 tcp:5004`, then `flutter run --dart-define=API_BASE_URL=http://localhost:5004`. Run `adb reverse` again after every re-plug. |
| Real phone, same Wi-Fi | start the backend with `ASPNETCORE_URLS=http://0.0.0.0:5004`, allow port 5004 in the firewall, then `flutter run --dart-define=API_BASE_URL=http://<computer IP>:5004` |

Use a debug build (`flutter run`): the backend speaks plain HTTP, and Android blocks plain HTTP by default in a release build.
