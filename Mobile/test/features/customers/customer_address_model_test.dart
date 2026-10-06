import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/customers/models/customer_address.dart';

void main() {
  group('CustomerAddress Model & HousingType Tests', () {
    test('HousingType maps api values and display names correctly', () {
      expect(HousingType.fromApiValue('APARTMENT'), HousingType.apartment);
      expect(HousingType.fromApiValue('HOUSE'), HousingType.house);
      expect(HousingType.fromApiValue('ROOM'), HousingType.room);
      expect(HousingType.fromApiValue('unknown'), HousingType.apartment);
      expect(HousingType.fromApiValue(null), HousingType.apartment);

      expect(HousingType.apartment.displayName, 'Căn hộ chung cư');
      expect(HousingType.house.displayName, 'Nhà riêng / Nhà phố');
      expect(HousingType.room.displayName, 'Phòng trọ');

      expect(HousingType.apartment.maxFloors, 1);
      expect(HousingType.house.maxFloors, 10);
      expect(HousingType.room.maxFloors, 1);
    });

    test('calculateTotalArea computes S_total = S_sàn x N_tầng accurately', () {
      expect(CustomerAddress.calculateTotalArea(60.0, 2), 120.0);
      expect(CustomerAddress.calculateTotalArea(75.5, 3), 226.5);
      expect(CustomerAddress.calculateTotalArea(25.0, 1), 25.0);
      expect(CustomerAddress.calculateTotalArea(0.0, 1), 0.0);
      expect(CustomerAddress.calculateTotalArea(50.0, 0), 0.0);
    });

    test('CustomerAddress deserializes from JSON and serializes to JSON', () {
      final json = {
        'addressId': 42,
        'label': 'Nhà riêng Ba Đình',
        'addressLine': '10 Liễu Giai, P. Cống Vị',
        'district': 'Ba Đình',
        'city': 'Hà Nội',
        'housingType': 'HOUSE',
        'floorAreaM2': 55.5,
        'numFloors': 3,
        'totalAreaM2': 166.5,
        'bedrooms': 3,
        'bathrooms': 2,
        'latitude': 21.0333,
        'longitude': 105.8145,
        'isDefault': true,
        'createdAt': '2026-10-06T10:00:00Z',
      };

      final address = CustomerAddress.fromJson(json);

      expect(address.addressId, 42);
      expect(address.label, 'Nhà riêng Ba Đình');
      expect(address.addressLine, '10 Liễu Giai, P. Cống Vị');
      expect(address.district, 'Ba Đình');
      expect(address.city, 'Hà Nội');
      expect(address.housingType, HousingType.house);
      expect(address.floorAreaM2, 55.5);
      expect(address.numFloors, 3);
      expect(address.totalAreaM2, 166.5);
      expect(address.bedrooms, 3);
      expect(address.bathrooms, 2);
      expect(address.latitude, 21.0333);
      expect(address.longitude, 105.8145);
      expect(address.isDefault, isTrue);
      expect(address.fullAddress, '10 Liễu Giai, P. Cống Vị, Ba Đình, Hà Nội');

      final serialized = address.toJson();
      expect(serialized['label'], 'Nhà riêng Ba Đình');
      expect(serialized['housingType'], 'HOUSE');
      expect(serialized['floorAreaM2'], 55.5);
      expect(serialized['numFloors'], 3);
      expect(serialized['isDefault'], isTrue);
    });

    test('copyWith produces updated clone without mutating original', () {
      const original = CustomerAddress(
        addressId: 1,
        label: 'Cũ',
        addressLine: '123 Đường',
        district: 'Q1',
        city: 'HCM',
        housingType: HousingType.room,
        floorAreaM2: 25.0,
        numFloors: 1,
        totalAreaM2: 25.0,
        latitude: 10.0,
        longitude: 106.0,
        isDefault: false,
      );

      final modified = original.copyWith(
        label: 'Mới',
        isDefault: true,
      );

      expect(modified.label, 'Mới');
      expect(modified.isDefault, isTrue);
      expect(modified.addressId, original.addressId);
      expect(original.label, 'Cũ');
      expect(original.isDefault, isFalse);
    });
  });
}
