import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/customers/widgets/admin_unit_picker_field.dart';

Widget _host({
  required bool labelAbove,
  required ValueChanged<String> onSelected,
}) {
  return MaterialApp(
    theme: NordicTheme.lightTheme,
    home: Scaffold(
      body: Padding(
        padding: const EdgeInsets.all(16),
        child: AdminUnitPickerField(
          key: const Key('picker'),
          label: 'Phường / Xã',
          value: null,
          options: const ['Phường An Khánh', 'Phường Bến Thành'],
          requiredMessage: 'Chọn phường / xã.',
          notInListMessage: 'Chọn lại.',
          labelAbove: labelAbove,
          onSelected: onSelected,
        ),
      ),
    ),
  );
}

void main() {
  group('AdminUnitPickerField label', () {
    testWidgets('labelAbove shows the label as a text above the field', (
      tester,
    ) async {
      await tester.pumpWidget(_host(labelAbove: true, onSelected: (_) {}));

      final label = tester.getRect(find.text('Phường / Xã'));
      final field = tester.getRect(find.byType(TextFormField));
      expect(label.bottom, lessThanOrEqualTo(field.top));
      // the field itself carries no floating label
      expect(find.widgetWithText(TextFormField, 'Phường / Xã'), findsNothing);
    });

    testWidgets(
      'labelAbove: tapping the field still opens the list and picks',
      (tester) async {
        final picked = <String>[];
        await tester.pumpWidget(
          _host(labelAbove: true, onSelected: picked.add),
        );

        await tester.tap(find.byType(TextFormField));
        await tester.pumpAndSettle();
        await tester.tap(find.widgetWithText(ListTile, 'Phường Bến Thành'));
        await tester.pumpAndSettle();

        expect(picked, ['Phường Bến Thành']);
      },
    );

    testWidgets(
      'by default the label floats in the field, as in the address form',
      (tester) async {
        await tester.pumpWidget(_host(labelAbove: false, onSelected: (_) {}));

        expect(
          find.widgetWithText(TextFormField, 'Phường / Xã'),
          findsOneWidget,
        );
      },
    );
  });
}
