import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/customers/screens/address_list_screen.dart';
import 'package:mobile/features/customers/services/customer_address_service.dart';
import 'package:mobile/features/identity/services/token_storage.dart';

import '../identity/fake_backend.dart';

void main() {
  testWidgets('a 401 from the server is shown on the address list, no mock address appears', (tester) async {
    tester.view.physicalSize = const Size(390, 844);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(() {
      tester.view.resetPhysicalSize();
      tester.view.resetDevicePixelRatio();
    });

    // flutter_test answers every HttpClient request with 400 by default; this test needs the real loopback server.
    final previousOverrides = HttpOverrides.current;
    HttpOverrides.global = null;
    addTearDown(() => HttpOverrides.global = previousOverrides);

    final backend = FakeBackend();
    await tester.runAsync(() async {
      await backend.start();
      backend.on('GET', '/api/customers/me/addresses', 401, FakeBackend.error('Phiên đăng nhập đã hết hạn.'));

      await tester.pumpWidget(MaterialApp(
        theme: NordicTheme.lightTheme,
        home: AddressListScreen(
          addressService: CustomerAddressService(baseUrl: backend.baseUrl, tokenStorage: TokenStorage()..clear()),
        ),
      ));
      // let the real request complete outside the fake clock
      await Future<void>.delayed(const Duration(milliseconds: 500));
    });
    await tester.pump();

    expect(find.textContaining('Phiên đăng nhập đã hết hạn.'), findsOneWidget);
    expect(find.text('Nhà riêng'), findsNothing); // seeded mock address of the old fallback
    expect(find.text('Căn hộ Sunrise'), findsNothing);

    await tester.runAsync(backend.stop);
  });
}
