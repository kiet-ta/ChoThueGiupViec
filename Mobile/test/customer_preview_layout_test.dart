import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'customer_preview.dart';

void main() {
  const screens = [
    'home',
    'home-empty',
    'home-no-address',
    'orders',
    'orders-empty',
    'detail-pending',
    'detail-assigned',
    'detail-completed',
    'detail-cancelled',
    'favorites',
    'account',
    'booking',
    'payment',
    'acceptance',
    'rating',
    'dispute',
    'dispute-list',
    'profile',
    'addresses',
    'address-form',
  ];
  for (final width in [360.0, 428.0]) {
    for (final keyboard in [false, true]) {
      for (final screen in screens) {
        testWidgets(
          '$screen fits $width at text scale 1.3, keyboard $keyboard',
          (tester) async {
            tester.view.physicalSize = Size(width, 844);
            tester.view.devicePixelRatio = 1;
            addTearDown(() {
              tester.view.resetPhysicalSize();
              tester.view.resetDevicePixelRatio();
            });
            await tester.pumpWidget(
              customerPreview(
                screen,
                textScale: 1.3,
                keyboard: keyboard,
                reducedMotion: true,
              ),
            );
            await tester.pump();
            await tester.pump(const Duration(milliseconds: 500));
            expect(tester.takeException(), isNull);
            await tester.pumpWidget(const SizedBox.shrink());
            await tester.pump();
          },
        );
      }
    }
  }
}
