import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/theme/nordic_colors.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/core/widgets/nordic_button.dart';

Widget _host(NordicButton button) => MaterialApp(
  theme: NordicTheme.lightTheme,
  home: Scaffold(body: Center(child: button)),
);

void main() {
  group('NordicButton danger variant (MOB-ORDER-01)', () {
    testWidgets('is a red filled button with a white label', (tester) async {
      await tester.pumpWidget(
        _host(
          NordicButton(
            label: 'Huỷ đơn',
            variant: NordicButtonVariant.danger,
            onPressed: () {},
          ),
        ),
      );

      final button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.style!.backgroundColor!.resolve({}), NordicColors.error);
      expect(button.style!.foregroundColor!.resolve({}), Colors.white);
      expect(find.text('Huỷ đơn'), findsOneWidget);
    });

    testWidgets('calls back when tapped', (tester) async {
      var taps = 0;
      await tester.pumpWidget(
        _host(
          NordicButton(
            label: 'Huỷ đơn',
            variant: NordicButtonVariant.danger,
            onPressed: () => taps++,
          ),
        ),
      );

      await tester.tap(find.text('Huỷ đơn'));
      await tester.pump();

      expect(taps, 1);
    });

    testWidgets('while loading it shows a spinner and ignores taps', (
      tester,
    ) async {
      var taps = 0;
      await tester.pumpWidget(
        _host(
          NordicButton(
            label: 'Huỷ đơn',
            variant: NordicButtonVariant.danger,
            isLoading: true,
            onPressed: () => taps++,
          ),
        ),
      );

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
      expect(find.text('Huỷ đơn'), findsNothing);
      await tester.tap(find.byType(ElevatedButton), warnIfMissed: false);
      await tester.pump();
      expect(taps, 0);
    });

    testWidgets('without a callback it is disabled', (tester) async {
      await tester.pumpWidget(
        _host(
          const NordicButton(
            label: 'Huỷ đơn',
            variant: NordicButtonVariant.danger,
            onPressed: null,
          ),
        ),
      );

      final button = tester.widget<ElevatedButton>(find.byType(ElevatedButton));
      expect(button.onPressed, isNull);
    });

    testWidgets('has no green call-to-action shadow around it', (tester) async {
      await tester.pumpWidget(
        _host(
          NordicButton(
            label: 'Huỷ đơn',
            variant: NordicButtonVariant.danger,
            onPressed: () {},
          ),
        ),
      );

      // the primary button wraps itself in a DecoratedBox with the cta shadow; the danger one does not
      expect(
        find.descendant(
          of: find.byType(NordicButton),
          matching: find.byWidgetPredicate(
            (w) =>
                w is DecoratedBox &&
                (w.decoration is BoxDecoration) &&
                (w.decoration as BoxDecoration).boxShadow != null,
          ),
        ),
        findsNothing,
      );
    });
  });
}
