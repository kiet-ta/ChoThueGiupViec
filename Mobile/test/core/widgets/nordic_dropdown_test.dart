import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:mobile/core/widgets/nordic_dropdown.dart';

Widget _host({
  required String value,
  required ValueChanged<String> onChanged,
  bool enabled = true,
  String? label,
}) {
  return MaterialApp(
    home: Scaffold(
      body: Padding(
        padding: const EdgeInsets.all(16.0),
        child: NordicDropdown<String>(
          key: const Key('under-test'),
          label: label,
          value: value,
          enabled: enabled,
          options: const [
            NordicDropdownOption('A', 'Tất cả'),
            NordicDropdownOption('B', 'Tiền công'),
            NordicDropdownOption('C', 'Rút tiền'),
            NordicDropdownOption('D', 'Khấu trừ'),
          ],
          onChanged: onChanged,
        ),
      ),
    ),
  );
}

void main() {
  group('NordicDropdown', () {
    testWidgets(
      'shows only the chosen option and the label until it is opened',
      (tester) async {
        await tester.pumpWidget(
          _host(value: 'B', label: 'Loại giao dịch', onChanged: (_) {}),
        );

        expect(find.text('Loại giao dịch'), findsOneWidget);
        expect(find.text('Tiền công'), findsOneWidget);
        expect(find.text('Rút tiền'), findsNothing);
        expect(find.text('Khấu trừ'), findsNothing);
      },
    );

    testWidgets('opens the list, calls back with the picked value', (
      tester,
    ) async {
      final picked = <String>[];
      await tester.pumpWidget(_host(value: 'A', onChanged: picked.add));

      await tester.tap(find.byKey(const Key('under-test')));
      await tester.pumpAndSettle();
      expect(find.text('Rút tiền'), findsOneWidget);
      expect(find.text('Khấu trừ'), findsOneWidget);

      await tester.tap(find.text('Rút tiền'));
      await tester.pumpAndSettle();

      expect(picked, ['C']);
    });

    testWidgets('does not call back when the chosen option is picked again', (
      tester,
    ) async {
      final picked = <String>[];
      await tester.pumpWidget(_host(value: 'A', onChanged: picked.add));

      await tester.tap(find.byKey(const Key('under-test')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Tất cả').last);
      await tester.pumpAndSettle();

      expect(picked, isEmpty);
    });

    testWidgets('follows a new value given by the parent', (tester) async {
      await tester.pumpWidget(_host(value: 'A', onChanged: (_) {}));
      expect(find.text('Tất cả'), findsOneWidget);

      await tester.pumpWidget(_host(value: 'D', onChanged: (_) {}));
      await tester.pumpAndSettle();

      expect(find.text('Khấu trừ'), findsOneWidget);
      expect(find.text('Tất cả'), findsNothing);
    });

    testWidgets('a disabled dropdown does not open', (tester) async {
      final picked = <String>[];
      await tester.pumpWidget(
        _host(value: 'A', enabled: false, onChanged: picked.add),
      );

      await tester.tap(find.byKey(const Key('under-test')));
      await tester.pumpAndSettle();

      expect(find.text('Rút tiền'), findsNothing);
      expect(picked, isEmpty);
    });
  });
}
