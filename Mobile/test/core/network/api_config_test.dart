import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/network/api_config.dart';
import 'package:mobile/features/customers/services/customer_address_service.dart';
import 'package:mobile/features/customers/services/customer_profile_service.dart';
import 'package:mobile/features/customers/services/favorite_worker_service.dart';
import 'package:mobile/features/identity/services/auth_service.dart';

/// MOB-NET-01: one constant, `API_BASE_URL`, is the backend address of every service.
/// It is a compile-time value: run `flutter test --dart-define=API_BASE_URL=http://localhost:5004 test/core/network/api_config_test.dart`
/// to see the same checks with another address (the expected value is read from the same define).
void main() {
  const expected = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'http://10.0.2.2:5004',
  );

  test('the address is the emulator alias unless a define changes it', () {
    expect(apiBaseUrl, expected);
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
}
