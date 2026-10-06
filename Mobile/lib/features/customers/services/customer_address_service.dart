import 'dart:io';
import '../../identity/services/api_error.dart';
import '../../identity/services/token_storage.dart';
import '../models/customer_address.dart';

/// Service managing customer address book matching .spec/contracts/customers.md §2.2.
///
/// [isMock] (only when a caller, a test, passes it): in-memory store, no network.
/// Off (the default, the real app): every call goes to the backend and a server or network error is thrown as
/// [ApiException]; it is never replaced by mock data.
class CustomerAddressService {
  final String baseUrl;
  final HttpClient _httpClient;
  final TokenStorage _tokenStorage;
  final bool isMock;

  // In-memory store, used only in mock mode
  final List<CustomerAddress> _mockStore = [];
  int _mockIdCounter = 100;

  CustomerAddressService({
    String? baseUrl,
    HttpClient? httpClient,
    TokenStorage? tokenStorage,
    this.isMock = false,
  })  : baseUrl = baseUrl ?? 'http://10.0.2.2:5004',
        _httpClient = httpClient ?? HttpClient(),
        _tokenStorage = tokenStorage ?? TokenStorage() {
    if (isMock) _seedDefaultMockData();
  }

  void _seedDefaultMockData() {
    _mockStore.addAll([
      CustomerAddress(
        addressId: 1,
        label: 'Nhà riêng',
        addressLine: '123 Nguyễn Thị Minh Khai, P. Bến Thành',
        district: 'Quận 1',
        city: 'TP. Hồ Chí Minh',
        housingType: HousingType.house,
        floorAreaM2: 60.0,
        numFloors: 2,
        totalAreaM2: 120.0,
        bedrooms: 3,
        bathrooms: 2,
        latitude: 10.7769,
        longitude: 106.7009,
        isDefault: true,
        createdAt: DateTime.now().subtract(const Duration(days: 10)),
      ),
      CustomerAddress(
        addressId: 2,
        label: 'Căn hộ Sunrise',
        addressLine: '27 Nguyễn Hữu Thọ, P. Tân Hưng',
        district: 'Quận 7',
        city: 'TP. Hồ Chí Minh',
        housingType: HousingType.apartment,
        floorAreaM2: 78.5,
        numFloors: 1,
        totalAreaM2: 78.5,
        bedrooms: 2,
        bathrooms: 2,
        latitude: 10.7412,
        longitude: 106.7028,
        isDefault: false,
        createdAt: DateTime.now().subtract(const Duration(days: 5)),
      ),
    ]);
  }

  /// Expose mock storage for deterministic widget/unit testing
  List<CustomerAddress> get mockStore => _mockStore;

  /// Clear mock data (for testing)
  void clearMockStore() {
    _mockStore.clear();
  }

  /// Add mock item directly (for testing)
  void addMockAddress(CustomerAddress address) {
    _mockStore.add(address);
  }

  Future<Map<String, dynamic>> _call(String method, String path, String fallbackMessage, {Object? body}) =>
      apiRequest(
        _httpClient,
        method,
        Uri.parse('$baseUrl/api/customers/me/addresses$path'),
        bearerToken: _tokenStorage.accessToken,
        body: body,
        fallbackMessage: fallbackMessage,
      );

  /// GET /api/customers/me/addresses
  Future<List<CustomerAddress>> getAddresses() async {
    if (!isMock) {
      final envelope = await _call('GET', '', 'Không tải được sổ địa chỉ.');
      final data = envelope['data'] as List<dynamic>? ?? [];
      return data.map((item) => CustomerAddress.fromJson(item as Map<String, dynamic>)).toList();
    }

    // Sort default first, then newest
    final sorted = List<CustomerAddress>.from(_mockStore);
    sorted.sort((a, b) {
      if (a.isDefault && !b.isDefault) return -1;
      if (!a.isDefault && b.isDefault) return 1;
      return (b.createdAt ?? DateTime(2000)).compareTo(a.createdAt ?? DateTime(2000));
    });
    return sorted;
  }

  /// GET /api/customers/me/addresses/{addressId}; null when the address does not exist (404).
  Future<CustomerAddress?> getAddressById(int addressId) async {
    if (!isMock) {
      try {
        final envelope = await _call('GET', '/$addressId', 'Không tải được địa chỉ.');
        final data = envelope['data'] as Map<String, dynamic>?;
        return data == null ? null : CustomerAddress.fromJson(data);
      } on ApiException catch (e) {
        if (e.statusCode == 404) return null;
        rethrow;
      }
    }

    try {
      return _mockStore.firstWhere((a) => a.addressId == addressId);
    } catch (_) {
      return null;
    }
  }

  /// POST /api/customers/me/addresses
  Future<CustomerAddress> createAddress(CustomerAddress input) async {
    _validateAddressInput(input);

    if (!isMock) {
      final envelope = await _call('POST', '', 'Tạo địa chỉ thất bại.', body: input.toJson());
      return CustomerAddress.fromJson(envelope['data'] as Map<String, dynamic>);
    }

    // Mock creation logic
    final isFirst = _mockStore.isEmpty;
    final shouldBeDefault = isFirst || input.isDefault;

    if (shouldBeDefault) {
      for (var i = 0; i < _mockStore.length; i++) {
        if (_mockStore[i].isDefault) {
          _mockStore[i] = _mockStore[i].copyWith(isDefault: false);
        }
      }
    }

    final total = CustomerAddress.calculateTotalArea(input.floorAreaM2, input.numFloors);
    final created = input.copyWith(
      addressId: ++_mockIdCounter,
      totalAreaM2: total,
      isDefault: shouldBeDefault,
      createdAt: DateTime.now(),
    );

    _mockStore.insert(0, created);
    return created;
  }

  /// PUT /api/customers/me/addresses/{addressId}
  Future<CustomerAddress> updateAddress(int addressId, CustomerAddress input) async {
    _validateAddressInput(input);

    if (!isMock) {
      final envelope = await _call('PUT', '/$addressId', 'Cập nhật địa chỉ thất bại.', body: input.toJson());
      return CustomerAddress.fromJson(envelope['data'] as Map<String, dynamic>);
    }

    // Mock update logic
    final index = _mockStore.indexWhere((a) => a.addressId == addressId);
    if (index == -1) {
      throw ArgumentError('Không tìm thấy địa chỉ với mã $addressId');
    }

    if (input.isDefault) {
      for (var i = 0; i < _mockStore.length; i++) {
        if (i != index && _mockStore[i].isDefault) {
          _mockStore[i] = _mockStore[i].copyWith(isDefault: false);
        }
      }
    }

    final total = CustomerAddress.calculateTotalArea(input.floorAreaM2, input.numFloors);
    final updated = input.copyWith(
      addressId: addressId,
      totalAreaM2: total,
      createdAt: _mockStore[index].createdAt,
    );

    _mockStore[index] = updated;
    return updated;
  }

  /// DELETE /api/customers/me/addresses/{addressId}
  Future<void> deleteAddress(int addressId) async {
    if (!isMock) {
      await _call('DELETE', '/$addressId', 'Xoá địa chỉ thất bại.');
      return;
    }

    final index = _mockStore.indexWhere((a) => a.addressId == addressId);
    if (index != -1) {
      final wasDefault = _mockStore[index].isDefault;
      _mockStore.removeAt(index);

      // If default was deleted and items remain, first remaining becomes default
      if (wasDefault && _mockStore.isNotEmpty) {
        _mockStore[0] = _mockStore[0].copyWith(isDefault: true);
      }
    }
  }

  void _validateAddressInput(CustomerAddress input) {
    if (input.label.trim().isEmpty || input.label.length > 50) {
      throw ArgumentError('Tên nhãn địa chỉ bắt buộc từ 1 đến 50 ký tự.');
    }
    if (input.addressLine.trim().isEmpty || input.addressLine.length > 255) {
      throw ArgumentError('Địa chỉ chi tiết bắt buộc từ 1 đến 255 ký tự.');
    }
    if (input.district.trim().isEmpty || input.district.length > 100) {
      throw ArgumentError('Quận/Huyện bắt buộc từ 1 đến 100 ký tự.');
    }
    if (input.city.trim().isEmpty || input.city.length > 100) {
      throw ArgumentError('Tỉnh/Thành phố bắt buộc từ 1 đến 100 ký tự.');
    }
    if (input.floorAreaM2 <= 0 || input.floorAreaM2 > 9999.99) {
      throw ArgumentError('Diện tích sàn phải lớn hơn 0 và nhỏ hơn 10.000 m².');
    }
    if (input.housingType == HousingType.room && input.floorAreaM2 > 30.0) {
      throw ArgumentError('Phòng trọ diện tích tối đa là 30 m² (PRD §1.1).');
    }
    if (input.housingType == HousingType.apartment || input.housingType == HousingType.room) {
      if (input.numFloors != 1) {
        throw ArgumentError('Căn hộ và phòng trọ chỉ có 1 tầng.');
      }
    } else if (input.housingType == HousingType.house) {
      if (input.numFloors < 1 || input.numFloors > 10) {
        throw ArgumentError('Nhà riêng số tầng từ 1 đến 10 tầng.');
      }
    }
    if (input.latitude < -90.0 || input.latitude > 90.0) {
      throw ArgumentError('Vĩ độ (latitude) không hợp lệ (-90 đến 90).');
    }
    if (input.longitude < -180.0 || input.longitude > 180.0) {
      throw ArgumentError('Kinh độ (longitude) không hợp lệ (-180 đến 180).');
    }
  }
}
