import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/theme/nordic_colors.dart';
import 'package:mobile/core/theme/nordic_radius.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/home/customer_shell.dart';
import 'package:mobile/features/home/worker_shell.dart';

// MOB-UI-18: a message at the bottom (the "Đăng nhập thành công" of the sign-in, or any other SnackBar) must not lift the
// centre button docked in the bottom bar of the two shells.

const _placeholderTabs = [
  SizedBox.expand(),
  SizedBox.expand(),
  SizedBox.expand(),
  SizedBox.expand(),
];

Future<void> _expectCentreButtonStays(
  WidgetTester tester,
  Widget shell,
  Key buttonKey,
) async {
  await tester.binding.setSurfaceSize(const Size(390, 844));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  await tester.pumpWidget(
    MaterialApp(theme: NordicTheme.lightTheme, home: shell),
  );
  await tester.pumpAndSettle();

  final button = find.byKey(buttonKey);
  final before = tester.getCenter(button);

  ScaffoldMessenger.of(tester.element(button))
      .showSnackBar(const SnackBar(content: Text('Đăng nhập thành công')));
  await tester.pump();
  await tester.pump(
    const Duration(milliseconds: 800),
  ); // the SnackBar has finished coming in

  expect(find.text('Đăng nhập thành công'), findsOneWidget);
  expect(tester.getCenter(button), before);
}

void main() {
  test('the theme makes every SnackBar float, with the field radius and the accent colour', () {
    final theme = NordicTheme.lightTheme.snackBarTheme;
    expect(theme.behavior, SnackBarBehavior.floating);
    expect(
      theme.shape,
      const RoundedRectangleBorder(borderRadius: NordicRadius.fieldAll),
    );
    expect(theme.backgroundColor, NordicColors.secondaryAccent);
  });

  testWidgets(
    'a SnackBar does not lift the "Đặt ca" button of the customer shell',
    (tester) async {
      await _expectCentreButtonStays(
        tester,
        CustomerShell(onSignOut: () {}, tabs: _placeholderTabs),
        const Key('customer-shell-book'),
      );
    },
  );

  testWidgets(
    'a SnackBar does not lift the "Việc" button of the worker shell',
    (tester) async {
      await _expectCentreButtonStays(
        tester,
        WorkerShell(onSignOut: () {}, workerId: 1, tabs: _placeholderTabs),
        const Key('worker-shell-job'),
      );
    },
  );
}
