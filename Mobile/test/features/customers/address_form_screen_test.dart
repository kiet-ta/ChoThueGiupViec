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

    testWidgets('renders create form and calculates live S_total', (
      tester,
    ) async {
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

      expect(find.text('Thêm địa chỉ'), findsOneWidget);
      expect(find.byKey(const Key('area-total')), findsOneWidget);
      expect(find.text('Nhà riêng / Nhà phố'), findsOneWidget);
      expect(find.text('Căn hộ chung cư'), findsOneWidget);
      expect(find.text('Phòng trọ'), findsOneWidget);

      // Scroll slightly so chips and fields are comfortably visible above bottom bar
      await tester.drag(find.byType(ListView).first, const Offset(0, -200));
      await tester.pumpAndSettle();

      // Switch to Apartment -> floors locked to 1
      await tester.tap(find.text('Căn hộ chung cư'));
      await tester.pumpAndSettle();
      expect(find.text('Cố định 1 tầng'), findsOneWidget);

      // Switch to Room -> area capped to 30.0
      await tester.tap(find.text('Phòng trọ'));
      await tester.pumpAndSettle();
      expect(find.text('Phòng trọ tối đa 30 m²'), findsOneWidget);
      expect(find.textContaining('30,0 m²'), findsWidgets);
    });

    testWidgets('validates required fields and submits valid form', (
      tester,
    ) async {
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
      await tester.tap(find.text('Lưu địa chỉ'));
      await tester.pumpAndSettle();

      expect(find.text('Nhập tên gợi nhớ.'), findsOneWidget);
      expect(find.text('Nhập số nhà và tên đường.'), findsOneWidget);
      // The province starts at Ho Chi Minh City; the ward must be chosen.
      expect(find.text('Thành phố Hồ Chí Minh'), findsOneWidget);
      expect(find.text('Chọn phường / xã.'), findsOneWidget);

      // Fill valid values
      final textFields = find.byType(TextFormField);
      // Field 0: label
      await tester.enterText(textFields.at(0), 'Biệt thự Thảo Điền');
      // Field 1: addressLine
      await tester.enterText(textFields.at(1), '12 Quốc Hương');
      await tester.pumpAndSettle();
      await pickAdminUnit(tester, 'Phường / Xã', 'an khanh', 'Phường An Khánh');

      // Submit
      await tester.tap(find.text('Lưu địa chỉ'));
      await tester.pumpAndSettle();

      // Verify created in service
      final addresses = await service.getAddresses();
      final created = addresses.firstWhere(
        (a) => a.label == 'Biệt thự Thảo Điền',
      );
      expect(created.city, 'Thành phố Hồ Chí Minh');
      expect(created.district, 'Phường An Khánh');
    });

    testWidgets('a new province clears the ward and lists only its units', (
      tester,
    ) async {
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

      await pickAdminUnit(tester, 'Phường / Xã', 'sai gon', 'Phường Sài Gòn');
      expect(find.text('Phường Sài Gòn'), findsOneWidget);

      await pickAdminUnit(
        tester,
        'Tỉnh / Thành phố',
        'ha noi',
        'Thành phố Hà Nội',
      );
      expect(find.text('Thành phố Hà Nội'), findsOneWidget);
      expect(find.text('Phường Sài Gòn'), findsNothing);

      // A ward of Ho Chi Minh City is not offered for Hanoi.
      await openPicker(tester, 'Phường / Xã');
      await tester.enterText(
        find.widgetWithText(TextField, searchHint),
        'sai gon',
      );
      await tester.pumpAndSettle();
      expect(find.text('Không tìm thấy. Thử gõ tên khác.'), findsOneWidget);
      await tester.enterText(
        find.widgetWithText(TextField, searchHint),
        'ba dinh',
      );
      await tester.pumpAndSettle();
      expect(find.text('Phường Ba Đình'), findsOneWidget);
    });

    testWidgets('renders edit form with initialAddress and updates it', (
      tester,
    ) async {
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

      expect(find.text('Sửa địa chỉ'), findsOneWidget);
      expect(find.text('Nhà Gốc'), findsOneWidget);
      expect(find.text('Lưu địa chỉ'), findsOneWidget);

      // The old free text values are shown as saved.
      expect(find.text('TP. Hồ Chí Minh'), findsOneWidget);
      expect(find.text('Quận 1'), findsOneWidget);

      // Edit label
      final textFields = find.byType(TextFormField);
      await tester.enterText(textFields.at(0), 'Nhà Gốc Đã Sửa');
      await tester.pumpAndSettle();

      // Values outside the catalogue must be chosen again before saving.
      await tester.tap(find.text('Lưu địa chỉ'));
      await tester.pumpAndSettle();
      expect(
        find.text('Chọn lại tỉnh / thành phố theo danh mục mới.'),
        findsOneWidget,
      );
      expect((await service.getAddressById(99))?.label, 'Nhà Gốc');

      await pickAdminUnit(
        tester,
        'Tỉnh / Thành phố',
        'ho chi minh',
        'Thành phố Hồ Chí Minh',
      );
      await pickAdminUnit(
        tester,
        'Phường / Xã',
        'ben thanh',
        'Phường Bến Thành',
      );

      await tester.tap(find.text('Lưu địa chỉ'));
      await tester.pumpAndSettle();

      final updated = await service.getAddressById(99);
      expect(updated?.label, 'Nhà Gốc Đã Sửa');
      expect(updated?.city, 'Thành phố Hồ Chí Minh');
      expect(updated?.district, 'Phường Bến Thành');
    });
  });
}

const searchHint = 'Tìm (gõ không dấu cũng được)';

Future<void> openPicker(WidgetTester tester, String fieldLabel) async {
  final field = find.widgetWithText(TextFormField, fieldLabel);
  await tester.ensureVisible(field);
  await tester.pumpAndSettle();
  await tester.tap(field);
  await tester.pumpAndSettle();
}

/// Opens the picker labelled [fieldLabel], searches [query] and picks [option].
Future<void> pickAdminUnit(
  WidgetTester tester,
  String fieldLabel,
  String query,
  String option,
) async {
  await openPicker(tester, fieldLabel);
  await tester.enterText(find.widgetWithText(TextField, searchHint), query);
  await tester.pumpAndSettle();
  await tester.tap(find.widgetWithText(ListTile, option));
  await tester.pumpAndSettle();
}
