import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/customers/models/customer_address.dart';
import 'package:mobile/features/customers/services/customer_address_service.dart';
import 'package:mobile/features/customers/services/customer_profile_service.dart';
import 'package:mobile/features/customers/services/favorite_worker_service.dart';
import 'package:mobile/features/identity/models/auth_models.dart';
import 'package:mobile/features/identity/services/api_error.dart';
import 'package:mobile/features/identity/services/token_storage.dart';

import '../identity/fake_backend.dart';

/// Customer services against a real HTTP server, with the default (non-mock) mode of the real app.
void main() {
  late FakeBackend backend;
  late TokenStorage storage;

  setUp(() async {
    backend = FakeBackend();
    await backend.start();
    storage = TokenStorage()
      ..clear()
      ..saveAuth(
        const AuthResult(
          tokenType: 'Bearer',
          accessToken: 'tok-123',
          accessTokenExpiresInSeconds: 900,
          refreshToken: 'ref',
          user: AuthUser(id: 1, role: 'Customer', isNewUser: false),
        ),
      );
  });

  tearDown(() => backend.stop());

  Matcher apiError(int status, [Object? message]) {
    var m = isA<ApiException>().having(
      (e) => e.statusCode,
      'statusCode',
      status,
    );
    if (message != null) m = m.having((e) => e.message, 'message', message);
    return throwsA(m);
  }

  const addressJson = {
    'addressId': 55,
    'label': 'Nhà',
    'addressLine': '1 Lê Lợi',
    'district': 'Quận 1',
    'city': 'TP. Hồ Chí Minh',
    'housingType': 'HOUSE',
    'floorAreaM2': 50,
    'numFloors': 2,
    'totalAreaM2': 100,
    'latitude': 10.77,
    'longitude': 106.7,
    'isDefault': true,
  };

  group('CustomerAddressService', () {
    CustomerAddressService service([String? baseUrl]) => CustomerAddressService(
      baseUrl: baseUrl ?? backend.baseUrl,
      tokenStorage: storage,
    );

    test('the real app has no mock data', () {
      expect(CustomerAddressService().isMock, isFalse);
      expect(CustomerAddressService().mockStore, isEmpty);
    });

    test('200 lists the server addresses and sends the Bearer token', () async {
      backend.on(
        'GET',
        '/api/v1/customers/me/addresses',
        200,
        FakeBackend.ok([addressJson]),
      );

      final list = await service().getAddresses();

      expect(list.map((a) => a.addressId), [55]);
      expect(list.single.totalAreaM2, 100);
      expect(
        backend.authHeaders['GET /api/v1/customers/me/addresses'],
        'Bearer tok-123',
      );
    });

    test('401 on the list throws the server message instead of showing mock addresses', () async {
      backend.on(
        'GET',
        '/api/v1/customers/me/addresses',
        401,
        FakeBackend.error('Phiên đăng nhập đã hết hạn.'),
      );

      await expectLater(
        service().getAddresses(),
        apiError(401, 'Phiên đăng nhập đã hết hạn.'),
      );
    });

    test(
      '400 on create throws with data.errors instead of reporting it saved',
      () async {
        backend.on(
          'POST',
          '/api/v1/customers/me/addresses',
          400,
          FakeBackend.error('Dữ liệu không hợp lệ.', {
            'errors': {
              'floorAreaM2': ['Diện tích phải lớn hơn 0.'],
            },
          }),
        );
        final input = CustomerAddress.fromJson(addressJson)
            .copyWith(addressId: 0);

        await expectLater(
          service().createAddress(input),
          throwsA(
            isA<ApiException>()
                .having((e) => e.statusCode, 'statusCode', 400)
                .having((e) => e.fieldErrors, 'fieldErrors', {
                  'floorAreaM2': ['Diện tích phải lớn hơn 0.'],
                }),
          ),
        );
      },
    );

    test('500 on delete throws instead of removing locally', () async {
      backend.on(
        'DELETE',
        '/api/v1/customers/me/addresses/55',
        500,
        FakeBackend.error('Lỗi máy chủ.'),
      );

      await expectLater(
        service().deleteAddress(55),
        apiError(500, 'Lỗi máy chủ.'),
      );
    });

    test('backend unreachable throws a network error', () async {
      await expectLater(
        service(FakeBackend.unreachableBaseUrl).getAddresses(),
        apiError(0),
      );
    });

    test('reverseGeocode calls POST /api/v1/locations/reverse-geocode with Bearer token', () async {
      backend.on(
        'POST',
        '/api/v1/locations/reverse-geocode',
        200,
        FakeBackend.ok({
          'city': 'Thành phố Hồ Chí Minh',
          'district': 'Phường Bến Nghé',
          'addressLine': '123 Lê Lợi, Quận 1',
          'formattedAddress':
              '123 Lê Lợi, Phường Bến Nghé, Quận 1, Thành phố Hồ Chí Minh',
          'latitude': 10.7769,
          'longitude': 106.7009,
        }),
      );

      final result = await service().reverseGeocode(10.7769, 106.7009);

      expect(result.city, 'Thành phố Hồ Chí Minh');
      expect(result.district, 'Phường Bến Nghé');
      expect(result.addressLine, '123 Lê Lợi, Quận 1');
      expect(result.latitude, 10.7769);
      expect(result.longitude, 106.7009);
      expect(
        backend.authHeaders['POST /api/v1/locations/reverse-geocode'],
        'Bearer tok-123',
      );
    });

    test('reverseGeocode throws on 401 unauthenticated', () async {
      backend.on(
        'POST',
        '/api/v1/locations/reverse-geocode',
        401,
        FakeBackend.error('Phiên đăng nhập đã hết hạn.'),
      );

      await expectLater(
        service().reverseGeocode(10.7769, 106.7009),
        apiError(401, 'Phiên đăng nhập đã hết hạn.'),
      );
    });
  });

  group('FavoriteWorkerService', () {
    FavoriteWorkerService service([String? baseUrl]) => FavoriteWorkerService(
      baseUrl: baseUrl ?? backend.baseUrl,
      tokenStorage: storage,
    );

    test('the real app has no mock data', () {
      expect(FavoriteWorkerService().mockStore, isEmpty);
    });

    test('200 lists the server favorites', () async {
      backend.on(
        'GET',
        '/api/v1/customers/me/favorite-workers',
        200,
        FakeBackend.ok([
          {
            'workerId': 9,
            'fullName': 'Phạm Thị Hoa',
            'ratingAvg': 4.7,
            'completedJobs': 12,
            'workStatus': 'ACTIVE',
          },
        ]),
      );

      final list = await service().getFavoriteWorkers();

      expect(list.single.workerId, 9);
      expect(
        backend.authHeaders['GET /api/v1/customers/me/favorite-workers'],
        'Bearer tok-123',
      );
    });

    test('401 on the list throws instead of showing mock workers', () async {
      backend.on(
        'GET',
        '/api/v1/customers/me/favorite-workers',
        401,
        FakeBackend.error('Chưa đăng nhập.'),
      );

      await expectLater(
        service().getFavoriteWorkers(),
        apiError(401, 'Chưa đăng nhập.'),
      );
    });

    test('404 on add throws instead of inventing a worker', () async {
      backend.on(
        'PUT',
        '/api/v1/customers/me/favorite-workers/77',
        404,
        FakeBackend.error('Không tìm thấy thợ.'),
      );

      await expectLater(
        service().addFavoriteWorker(77),
        apiError(404, 'Không tìm thấy thợ.'),
      );
    });
  });

  group('CustomerProfileService', () {
    CustomerProfileService service([String? baseUrl]) => CustomerProfileService(
      baseUrl: baseUrl ?? backend.baseUrl,
      tokenStorage: storage,
    );

    test('200 returns the server profile', () async {
      backend.on(
        'GET',
        '/api/v1/customers/me',
        200,
        FakeBackend.ok({
          'customerId': 3,
          'phoneNumber': '0912345678',
          'fullName': 'Đỗ Minh Khang',
          'trustScore': 4.5,
          'accountStatus': 'ACTIVE',
        }),
      );

      final profile = await service().getProfile();

      expect(profile.fullName, 'Đỗ Minh Khang');
      expect(backend.authHeaders['GET /api/v1/customers/me'], 'Bearer tok-123');
    });

    test('401 on get throws instead of showing the mock profile', () async {
      backend.on(
        'GET',
        '/api/v1/customers/me',
        401,
        FakeBackend.error('Chưa đăng nhập.'),
      );

      await expectLater(
        service().getProfile(),
        apiError(401, 'Chưa đăng nhập.'),
      );
    });

    test('400 on update throws instead of updating locally', () async {
      backend.on(
        'PUT',
        '/api/v1/customers/me',
        400,
        FakeBackend.error('Email không hợp lệ.', {
          'errors': {
            'email': ['Email đã được dùng.'],
          },
        }),
      );

      await expectLater(
        service().updateProfile(fullName: 'Khang', email: 'k@example.com'),
        throwsA(
          isA<ApiException>().having(
            (e) => e.fieldErrors?['email'],
            'email errors',
            ['Email đã được dùng.'],
          ),
        ),
      );
      expect(backend.requestBodies['PUT /api/v1/customers/me'], {
        'fullName': 'Khang',
        'email': 'k@example.com',
      });
    });

    test('backend unreachable throws a network error', () async {
      await expectLater(
        service(FakeBackend.unreachableBaseUrl).getProfile(),
        apiError(0),
      );
    });
  });
}
