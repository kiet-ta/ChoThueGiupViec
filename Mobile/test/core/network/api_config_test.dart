import 'package:flutter/foundation.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/network/api_config.dart';
import 'package:mobile/features/customers/services/customer_address_service.dart';
import 'package:mobile/features/customers/services/customer_profile_service.dart';
import 'package:mobile/features/customers/services/favorite_worker_service.dart';
import 'package:mobile/features/identity/services/auth_service.dart';

/// MOB-NET-01 / MOB-PLATFORM-01: one value, `API_BASE_URL` (or the platform default), is the backend address of every service.
/// It is a compile-time define: run `flutter test --dart-define=API_BASE_URL=http://localhost:5004 test/core/network/api_config_test.dart`
/// to see the same checks with another address.
void main() {
  test('the default is the emulator alias on Android, localhost elsewhere', () {
    String of(TargetPlatform platform, {bool web = false}) =>
        defaultApiBaseUrl(isWeb: web, platform: platform);

    expect(of(TargetPlatform.android), 'http://10.0.2.2:5004');
    expect(of(TargetPlatform.windows), 'http://localhost:5004');
    expect(of(TargetPlatform.macOS), 'http://localhost:5004');
    expect(of(TargetPlatform.linux), 'http://localhost:5004');
    expect(of(TargetPlatform.iOS), 'http://localhost:5004');
    // A browser on an Android phone is still a browser: 10.0.2.2 means nothing there.
    expect(of(TargetPlatform.android, web: true), 'http://localhost:5004');
  });

  test('the define wins over the platform default', () {
    const given = String.fromEnvironment('API_BASE_URL');
    expect(
      apiBaseUrl,
      given.isNotEmpty
          ? given
          : defaultApiBaseUrl(isWeb: kIsWeb, platform: defaultTargetPlatform),
    );
  });

  test('ApiClient and the services use the shared address', () {
    expect(ApiClient().baseUrl, apiBaseUrl);
    expect(AuthService().baseUrl, apiBaseUrl);
    expect(CustomerAddressService().baseUrl, apiBaseUrl);
    expect(CustomerProfileService().baseUrl, apiBaseUrl);
    expect(FavoriteWorkerService().baseUrl, apiBaseUrl);
  });

  test('a caller can still pass its own address (tests do)', () {
    expect(ApiClient(baseUrl: 'http://x:1').baseUrl, 'http://x:1');
    expect(AuthService(baseUrl: 'http://x:1').baseUrl, 'http://x:1');
  });

  test('on a test host (not Android) the default is localhost', () {
    // flutter test runs with defaultTargetPlatform = android unless a test overrides it.
    debugDefaultTargetPlatformOverride = TargetPlatform.windows;
    addTearDown(() => debugDefaultTargetPlatformOverride = null);
    const given = String.fromEnvironment('API_BASE_URL');
    if (given.isEmpty) expect(apiBaseUrl, 'http://localhost:5004');
  });
}
