import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/disputes/models/dispute_models.dart';
import 'package:mobile/features/disputes/screens/dispute_create_screen.dart';

// Its own file: a widget test binding answers every HttpClient request with 400, which would break the local-server tests.
void main() {
  testWidgets('the form by default offers to add a photo and does not say photos are unavailable', (tester) async {
    tester.view.physicalSize = const Size(390, 2200);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(() {
      tester.view.resetPhysicalSize();
      tester.view.resetDevicePixelRatio();
    });

    await tester.pumpWidget(MaterialApp(
      theme: NordicTheme.lightTheme,
      home: const DisputeCreateScreen(role: DisputeRole.customer, orderId: 42),
    ));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('dispute-add-photo')), findsOneWidget);
    expect(find.byKey(const Key('dispute-unavailable')), findsNothing);
  });
}
