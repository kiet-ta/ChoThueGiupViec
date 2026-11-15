import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/core/widgets/nordic_page.dart';

/// MOB-A11Y-01: the back arrow of a pushed page says "Quay lại" (not the English "Back") and goes back.
void main() {
  testWidgets('a pushed page has a "Quay lại" back arrow that pops', (
    tester,
  ) async {
    final semantics = tester.ensureSemantics();
    await tester.pumpWidget(
      MaterialApp(
        theme: NordicTheme.lightTheme,
        home: Builder(
          builder: (context) => TextButton(
            onPressed: () => Navigator.of(context).push(
              MaterialPageRoute<void>(
                builder: (_) =>
                    const NordicPage.pushed(title: 'Chi tiết', children: []),
              ),
            ),
            child: const Text('mở'),
          ),
        ),
      ),
    );
    await tester.tap(find.text('mở'));
    await tester.pumpAndSettle();

    expect(find.byTooltip('Quay lại'), findsOneWidget);
    expect(find.byType(BackButton), findsOneWidget);
    expect(find.byTooltip('Back'), findsNothing);

    await tester.tap(find.byKey(const Key('nordic-page-back')));
    await tester.pumpAndSettle();
    expect(find.text('mở'), findsOneWidget);
    semantics.dispose();
  });

  testWidgets('a page that cannot go back has no back arrow', (tester) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: NordicTheme.lightTheme,
        home: const NordicPage.pushed(title: 'Gốc', children: []),
      ),
    );

    expect(find.byKey(const Key('nordic-page-back')), findsNothing);
  });

  testWidgets('a screen keeps its own leading', (tester) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: NordicTheme.lightTheme,
        home: const NordicPage.pushed(
          title: 'Riêng',
          leading: Icon(Icons.close, key: Key('own-leading')),
          children: [],
        ),
      ),
    );

    expect(find.byKey(const Key('own-leading')), findsOneWidget);
    expect(find.byKey(const Key('nordic-page-back')), findsNothing);
  });
}
