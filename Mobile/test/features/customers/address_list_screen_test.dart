import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/customers/screens/address_form_screen.dart';
import 'package:mobile/features/customers/screens/address_list_screen.dart';
import 'package:mobile/features/customers/services/customer_address_service.dart';

void main() {
  group('AddressListScreen Widget Tests', () {
    late CustomerAddressService service;

    setUp(() {
      service = CustomerAddressService(isMock: true);
    });

    testWidgets('renders address list with items, badges, and S_total info', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: AddressListScreen(addressService: service),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Sổ Địa Chỉ'), findsOneWidget);
      expect(find.text('Địa chỉ của bạn'), findsOneWidget);
      expect(find.text('Nhà riêng'), findsOneWidget);
      expect(find.text('MẶC ĐỊNH'), findsOneWidget);
      expect(find.text('Thêm Địa Chỉ Mới'), findsOneWidget);
      expect(find.textContaining('S_total'), findsWidgets);
    });

    testWidgets('navigates to AddressFormScreen when tap Thêm Địa Chỉ Mới', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: AddressListScreen(addressService: service),
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.text('Thêm Địa Chỉ Mới'));
      await tester.pumpAndSettle();

      expect(find.byType(AddressFormScreen), findsOneWidget);
      expect(find.text('Thêm Địa Chỉ Mới'), findsWidgets);
    });

    testWidgets('displays empty state when no addresses exist', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      service.clearMockStore();

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: AddressListScreen(addressService: service),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Chưa Có Địa Chỉ Nào'), findsOneWidget);
      expect(find.text('Thêm Địa Chỉ Mới'), findsOneWidget);
    });

    testWidgets('shows delete confirmation dialog and deletes address', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: AddressListScreen(addressService: service),
        ),
      );
      await tester.pumpAndSettle();

      // Find first delete icon button
      final deleteButtons = find.byIcon(Icons.delete_outline_rounded);
      expect(deleteButtons, findsWidgets);

      await tester.tap(deleteButtons.first);
      await tester.pumpAndSettle();

      expect(find.text('Xác Nhận Xoá'), findsOneWidget);
      expect(find.text('Xoá'), findsOneWidget);

      await tester.tap(find.text('Xoá'));
      await tester.pumpAndSettle();

      // Check snackbar
      expect(find.textContaining('thành công'), findsOneWidget);
    });
  });
}
