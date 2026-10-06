import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/customers/models/customer_address.dart';
import 'package:mobile/features/customers/services/customer_address_service.dart';

void main() {
  group('CustomerAddressService Tests', () {
    late CustomerAddressService service;

    setUp(() {
      service = CustomerAddressService(isMock: true);
    });

    test('getAddresses returns seeded addresses with default sorted first', () async {
      final list = await service.getAddresses();
      expect(list.length, greaterThanOrEqualTo(2));
      expect(list.first.isDefault, isTrue);
    });

    test('createAddress calculates S_total and saves to storage', () async {
      const newAddress = CustomerAddress(
        addressId: 0,
        label: 'Nhà Phố Thủ Đức',
        addressLine: '50 Đặng Văn Bi, P. Trường Thọ',
        district: 'TP. Thủ Đức',
        city: 'TP. Hồ Chí Minh',
        housingType: HousingType.house,
        floorAreaM2: 70.0,
        numFloors: 3,
        totalAreaM2: 0.0, // Should be computed as 210.0
        latitude: 10.8492,
        longitude: 106.7725,
        isDefault: false,
      );

      final created = await service.createAddress(newAddress);
      expect(created.addressId, greaterThan(0));
      expect(created.totalAreaM2, 210.0);
      expect(created.label, 'Nhà Phố Thủ Đức');

      final all = await service.getAddresses();
      expect(all.any((a) => a.addressId == created.addressId), isTrue);
    });

    test('createAddress with isDefault=true clears previous default', () async {
      final initialList = await service.getAddresses();
      final previousDefault = initialList.firstWhere((a) => a.isDefault);

      const defaultAddress = CustomerAddress(
        addressId: 0,
        label: 'Cơ Quan Mới',
        addressLine: '99 Lê Thánh Tôn, P. Bến Nghé',
        district: 'Quận 1',
        city: 'TP. Hồ Chí Minh',
        housingType: HousingType.apartment,
        floorAreaM2: 50.0,
        numFloors: 1,
        totalAreaM2: 50.0,
        latitude: 10.7760,
        longitude: 106.7015,
        isDefault: true,
      );

      final created = await service.createAddress(defaultAddress);
      expect(created.isDefault, isTrue);

      final updatedList = await service.getAddresses();
      final oldAddr = updatedList.firstWhere((a) => a.addressId == previousDefault.addressId);
      expect(oldAddr.isDefault, isFalse);
    });

    test('validates housing type constraints and throws ArgumentError', () async {
      // 1. Room > 30 m2
      const invalidRoom = CustomerAddress(
        addressId: 0,
        label: 'Phòng trọ rộng',
        addressLine: '123 Hẻm',
        district: 'Quận 1',
        city: 'TP. Hồ Chí Minh',
        housingType: HousingType.room,
        floorAreaM2: 35.0, // > 30 m2 invalid
        numFloors: 1,
        totalAreaM2: 35.0,
        latitude: 10.0,
        longitude: 106.0,
      );
      expect(() => service.createAddress(invalidRoom), throwsArgumentError);

      // 2. Apartment with > 1 floors
      const multiFloorApartment = CustomerAddress(
        addressId: 0,
        label: 'Chung cư 2 tầng',
        addressLine: '123 Hẻm',
        district: 'Quận 1',
        city: 'TP. Hồ Chí Minh',
        housingType: HousingType.apartment,
        floorAreaM2: 80.0,
        numFloors: 2, // invalid for apartment
        totalAreaM2: 160.0,
        latitude: 10.0,
        longitude: 106.0,
      );
      expect(() => service.createAddress(multiFloorApartment), throwsArgumentError);

      // 3. House with > 10 floors
      const highRiseHouse = CustomerAddress(
        addressId: 0,
        label: 'Nhà 12 tầng',
        addressLine: '123 Hẻm',
        district: 'Quận 1',
        city: 'TP. Hồ Chí Minh',
        housingType: HousingType.house,
        floorAreaM2: 50.0,
        numFloors: 12, // > 10 invalid
        totalAreaM2: 600.0,
        latitude: 10.0,
        longitude: 106.0,
      );
      expect(() => service.createAddress(highRiseHouse), throwsArgumentError);
    });

    test('updateAddress updates record and recomputes totalAreaM2', () async {
      final list = await service.getAddresses();
      final target = list.first;

      final updated = await service.updateAddress(
        target.addressId,
        target.copyWith(
          floorAreaM2: 100.0,
          numFloors: 2,
        ),
      );

      expect(updated.floorAreaM2, 100.0);
      expect(updated.totalAreaM2, 200.0);
    });

    test('deleteAddress removes record and re-assigns default if needed', () async {
      final initial = await service.getAddresses();
      final defaultAddr = initial.firstWhere((a) => a.isDefault);

      await service.deleteAddress(defaultAddr.addressId);

      final remaining = await service.getAddresses();
      expect(remaining.any((a) => a.addressId == defaultAddr.addressId), isFalse);
      if (remaining.isNotEmpty) {
        expect(remaining.first.isDefault, isTrue);
      }
    });
  });
}
