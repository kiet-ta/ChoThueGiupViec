import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/core/widgets/grouped_list_card.dart';
import 'package:mobile/features/customers/screens/address_form_screen.dart';
import 'package:mobile/features/customers/screens/address_list_screen.dart';
import 'package:mobile/features/customers/services/customer_address_service.dart';

void main() {
  group('AddressListScreen Widget Tests', () {
    late CustomerAddressService service;

    setUp(() {
      service = CustomerAddressService(isMock: true);
    });

    testWidgets('renders address list with items, badges, and S_total info', (
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
          home: AddressListScreen(addressService: service),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Địa chỉ của tôi'), findsOneWidget);
      expect(find.text('Nhà riêng'), findsOneWidget);
      expect(find.text('Mặc định'), findsOneWidget);
      expect(find.text('Thêm địa chỉ'), findsOneWidget);
      expect(find.text('m²'), findsWidgets);
      expect(find.byType(GroupedListCard), findsOneWidget);
    });

    for (final width in [360.0, 428.0]) {
      testWidgets('address list fits at $width with larger text', (
        tester,
      ) async {
        tester.view.physicalSize = Size(width, 844);
        tester.view.devicePixelRatio = 1;
        addTearDown(() {
          tester.view.resetPhysicalSize();
          tester.view.resetDevicePixelRatio();
        });
        await tester.pumpWidget(
          MaterialApp(
            theme: NordicTheme.lightTheme,
            builder: (context, child) => MediaQuery(
              data: MediaQuery.of(context)
                  .copyWith(textScaler: const TextScaler.linear(1.3)),
              child: child!,
            ),
            home: AddressListScreen(addressService: service),
          ),
        );
        await tester.pumpAndSettle();
        expect(tester.takeException(), isNull);
      });
    }

    testWidgets('navigates to AddressFormScreen when tap Thêm Địa Chỉ Mới', (
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
          home: AddressListScreen(addressService: service),
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.text('Thêm địa chỉ'));
      await tester.pumpAndSettle();

      expect(find.byType(AddressFormScreen), findsOneWidget);
      expect(find.text('Thêm địa chỉ'), findsWidgets);
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

      expect(find.text('Chưa có địa chỉ nào'), findsOneWidget);
      expect(find.text('Thêm địa chỉ'), findsOneWidget);
    });

    testWidgets('shows delete confirmation dialog and deletes address', (
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
          home: AddressListScreen(addressService: service),
        ),
      );
      await tester.pumpAndSettle();

      // Find first delete icon button
      final deleteButtons = find.byIcon(Icons.delete_outline);
      expect(deleteButtons, findsWidgets);

      await tester.tap(deleteButtons.first);
      await tester.pumpAndSettle();

      expect(find.text('Xoá địa chỉ này?'), findsOneWidget);
      expect(find.text('Xoá'), findsOneWidget);

      await tester.tap(find.text('Xoá'));
      await tester.pumpAndSettle();

      // Check snackbar
      expect(find.textContaining('Đã xoá địa chỉ'), findsOneWidget);
    });

    testWidgets('a tap on a card makes it the default; the pencil edits', (
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
          home: AddressListScreen(addressService: service),
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('address-card-2')));
      await tester.pumpAndSettle();
      expect(
        service.mockStore.firstWhere((a) => a.addressId == 2).isDefault,
        isTrue,
      );
      expect(
        service.mockStore.firstWhere((a) => a.addressId == 1).isDefault,
        isFalse,
      );
      expect(find.byType(AddressFormScreen), findsNothing);

      await tester.tap(find.byKey(const Key('address-edit-1')));
      await tester.pumpAndSettle();
      expect(find.byType(AddressFormScreen), findsOneWidget);
    });

    testWidgets('opened by the booking form, a tap returns that address', (
      tester,
    ) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });
      Object? picked;
      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: Builder(
            builder: (context) => TextButton(
              key: const Key('open-list'),
              onPressed: () async {
                picked = await Navigator.of(context).push<Object?>(
                  MaterialPageRoute<Object?>(
                    builder: (_) => AddressListScreen(
                      addressService: service,
                      args: const AddressListArgs(selectedAddressId: 1),
                    ),
                  ),
                );
              },
              child: const Text('open'),
            ),
          ),
        ),
      );
      await tester.tap(find.byKey(const Key('open-list')));
      await tester.pumpAndSettle();

      expect(find.text('Chọn địa chỉ'), findsOneWidget);
      expect(find.byKey(const Key('address-selected-1')), findsOneWidget);
      await tester.tap(find.byKey(const Key('address-card-2')));
      await tester.pumpAndSettle();

      expect(picked, 2);
      expect(find.byType(AddressListScreen), findsNothing);
      // Picking for a booking does not change the default address.
      expect(
        service.mockStore.firstWhere((a) => a.addressId == 1).isDefault,
        isTrue,
      );
    });
  });
}
