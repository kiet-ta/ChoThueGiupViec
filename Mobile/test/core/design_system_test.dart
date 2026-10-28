import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/motion/nordic_motion.dart';
import 'package:mobile/core/theme/nordic_radius.dart';
import 'package:mobile/core/theme/nordic_spacing.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/core/theme/nordic_typography.dart';
import 'package:mobile/core/widgets/nordic_button.dart';
import 'package:mobile/core/widgets/nordic_card.dart';
import 'package:mobile/core/widgets/nordic_page.dart';
import 'package:mobile/core/widgets/pressable.dart';

Widget _app(Widget home) =>
    MaterialApp(theme: NordicTheme.lightTheme, home: home);

void main() {
  group('System Design tokens (Mobile/DESIGN-SYSTEM.md §3)', () {
    test('spacing is a 4-point scale with a 20 px gutter', () {
      for (final v in [
        NordicSpace.xxs,
        NordicSpace.xs,
        NordicSpace.sm,
        NordicSpace.md,
        NordicSpace.gutter,
        NordicSpace.lg,
        NordicSpace.section,
        NordicSpace.xl,
      ]) {
        expect(v % 4, 0.0, reason: '$v is on the 4-point grid');
      }
      expect(NordicSpace.gutter, 20.0);
    });

    test('shapes stay rounded: card 20, field 14, tile 12, sheet 28', () {
      expect(NordicRadius.card, 20.0);
      expect(NordicRadius.field, 14.0);
      expect(NordicRadius.tile, 12.0);
      expect(NordicRadius.sheet, 28.0);
      final theme = NordicTheme.lightTheme;
      expect(
        (theme.cardTheme.shape! as RoundedRectangleBorder).borderRadius,
        NordicRadius.cardAll,
      );
      expect(
        (theme.inputDecorationTheme.border! as OutlineInputBorder).borderRadius,
        NordicRadius.fieldAll,
      );
      expect(
        (theme.bottomSheetTheme.shape! as RoundedRectangleBorder).borderRadius,
        NordicRadius.sheetTop,
      );
      expect(
        theme.elevatedButtonTheme.style!.shape!.resolve({}),
        isA<StadiumBorder>(),
      );
    });
  });

  group('NordicPage', () {
    testWidgets('a tab screen shows a large display title, no app bar', (
      tester,
    ) async {
      await tester.pumpWidget(
        _app(
          const NordicPage.tab(
            title: 'Đơn',
            subtitle: 'Thứ Tư, 28/10',
            children: [Text('nội dung')],
          ),
        ),
      );
      expect(find.byType(AppBar), findsNothing);
      expect(
        tester.widget<Text>(find.text('Đơn')).style,
        NordicTypography.display,
      );
      expect(find.text('Thứ Tư, 28/10'), findsOneWidget);
    });

    testWidgets('a pushed screen has an app bar with back', (tester) async {
      await tester.pumpWidget(
        _app(
          Builder(
            builder: (context) => TextButton(
              onPressed: () => Navigator.of(context).push(
                MaterialPageRoute<void>(
                  builder: (_) => const NordicPage.pushed(
                    title: 'Chi tiết đơn',
                    children: [Text('nội dung')],
                  ),
                ),
              ),
              child: const Text('open'),
            ),
          ),
        ),
      );
      await tester.tap(find.text('open'));
      await tester.pumpAndSettle();
      expect(find.byType(AppBar), findsOneWidget);
      expect(find.text('Chi tiết đơn'), findsOneWidget);
      expect(find.byType(BackButton), findsOneWidget);
    });

    testWidgets('its actions stay above a 300 px keyboard', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      tester.view.viewInsets = const FakeViewPadding(bottom: 300);
      addTearDown(tester.view.reset);

      await tester.pumpWidget(
        _app(
          NordicPage.pushed(
            title: 'Địa chỉ',
            actions: [NordicButton(label: 'Lưu', onPressed: () {})],
            children: const [TextField()],
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(tester.getRect(find.text('Lưu')).bottom, lessThanOrEqualTo(544));
      expect(find.text('Lưu').hitTestable(), findsOneWidget);
    });
  });

  group('NordicCard', () {
    testWidgets('a tappable card shrinks on press and taps once', (
      tester,
    ) async {
      var taps = 0;
      await tester.pumpWidget(
        _app(
          Scaffold(
            body: Center(
              child: NordicCard(
                onTap: () => taps++,
                child: const SizedBox(width: 200, height: 60),
              ),
            ),
          ),
        ),
      );
      double scale() => tester
          .widget<Transform>(
            find.descendant(
              of: find.byType(Pressable),
              matching: find.byType(Transform),
            ),
          )
          .transform
          .storage[0];

      final gesture = await tester.startGesture(
        tester.getCenter(find.byType(NordicCard)),
      );
      await tester.pump();
      await tester.pump(NordicMotion.press);
      expect(scale(), lessThan(1.0));
      await gesture.up();
      await tester.pumpAndSettle();
      expect(taps, 1);
    });

    testWidgets('a plain card has no press feedback', (tester) async {
      await tester.pumpWidget(
        _app(
          const Scaffold(
            body: NordicCard(child: SizedBox(width: 200, height: 60)),
          ),
        ),
      );
      expect(find.byType(Pressable), findsNothing);
    });
  });
}
