import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/customers/models/customer_address.dart';
import 'package:mobile/features/customers/screens/address_form_screen.dart';
import 'package:mobile/features/customers/services/customer_address_service.dart';

void main() {
  group('AddressFormScreen Widget Tests', () {
    late CustomerAddressService service;

    setUp(() {
      service = CustomerAddressService(isMock: true);
    });

    testWidgets('renders create form and calculates live S_total', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: AddressFormScreen(addressService: service),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Thêm Địa Chỉ Mới'), findsOneWidget);
      expect(find.textContaining('CÔNG THỨC S_TOTAL'), findsOneWidget);
      expect(find.text('Nhà riêng / Nhà phố'), findsOneWidget);
      expect(find.text('Căn hộ chung cư'), findsOneWidget);
      expect(find.text('Phòng trọ'), findsOneWidget);

      // Scroll slightly so chips and fields are comfortably visible above bottom bar
      await tester.drag(find.byType(ListView), const Offset(0, -200));
      await tester.pumpAndSettle();

      // Switch to Apartment -> floors locked to 1
      await tester.tap(find.text('Căn hộ chung cư'));
      await tester.pumpAndSettle();
      expect(find.text('Cố định 1 tầng'), findsOneWidget);

      // Switch to Room -> area capped to 30.0
      await tester.tap(find.text('Phòng trọ'));
      await tester.pumpAndSettle();
      expect(find.text('Phòng trọ tối đa 30 m²'), findsOneWidget);
      expect(find.textContaining('30.0 m²'), findsWidgets);
    });

    testWidgets('validates required fields and submits valid form', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: AddressFormScreen(addressService: service),
        ),
      );
      await tester.pumpAndSettle();

      // Submit with empty label -> validation error
      await tester.tap(find.text('Tạo Địa Chỉ Mới'));
      await tester.pumpAndSettle();

      expect(find.text('Vui lòng nhập tên nhãn địa chỉ'), findsOneWidget);
      expect(find.text('Vui lòng nhập số nhà và tên đường'), findsOneWidget);

      // Fill valid values
      final textFields = find.byType(TextFormField);
      // Field 0: label
      await tester.enterText(textFields.at(0), 'Biệt thự Thảo Điền');
      // Field 1: addressLine
      await tester.enterText(textFields.at(1), '12 Quốc Hương, P. Thảo Điền');
      await tester.pumpAndSettle();

      // Submit
      await tester.tap(find.text('Tạo Địa Chỉ Mới'));
      await tester.pumpAndSettle();

      // Verify created in service
      final addresses = await service.getAddresses();
      expect(addresses.any((a) => a.label == 'Biệt thự Thảo Điền'), isTrue);
    });

    testWidgets('renders edit form with initialAddress and updates it', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      const initial = CustomerAddress(
        addressId: 99,
        label: 'Nhà Gốc',
        addressLine: '123 Gốc, P. 1',
        district: 'Quận 1',
        city: 'TP. Hồ Chí Minh',
        housingType: HousingType.house,
        floorAreaM2: 50.0,
        numFloors: 2,
        totalAreaM2: 100.0,
        latitude: 10.77,
        longitude: 106.70,
        isDefault: false,
      );
      service.addMockAddress(initial);

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: AddressFormScreen(
            initialAddress: initial,
            addressService: service,
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Chỉnh Sửa Địa Chỉ'), findsOneWidget);
      expect(find.text('Nhà Gốc'), findsOneWidget);
      expect(find.text('Lưu Thay Đổi'), findsOneWidget);

      // Edit label
      final textFields = find.byType(TextFormField);
      await tester.enterText(textFields.at(0), 'Nhà Gốc Đã Sửa');
      await tester.pumpAndSettle();

      await tester.tap(find.text('Lưu Thay Đổi'));
      await tester.pumpAndSettle();

      final updated = await service.getAddressById(99);
      expect(updated?.label, 'Nhà Gốc Đã Sửa');
    });
  });
}
