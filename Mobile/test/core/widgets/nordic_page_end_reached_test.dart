import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/core/widgets/nordic_page.dart';

/// MOB-PAGING-01: `NordicPage.onEndReached` asks for the next page near the end of a long list.
void main() {
  Future<void> pump(WidgetTester tester, int rows, VoidCallback onEnd) async {
    tester.view.physicalSize = const Size(390, 844);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.reset);
    await tester.pumpWidget(
      MaterialApp(
        theme: NordicTheme.lightTheme,
        home: NordicPage.pushed(
          title: 'Danh sách',
          onEndReached: onEnd,
          children: [
            for (var i = 0; i < rows; i++)
              SizedBox(height: 60, child: Text('row $i')),
          ],
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('a long list asks for more only once scrolled near its end', (
    tester,
  ) async {
    var calls = 0;
    await pump(tester, 60, () => calls++); // 3 600 px, about 4 screens

    expect(calls, 0);

    await tester.drag(find.byType(ListView), const Offset(0, -1000));
    await tester.pumpAndSettle();
    expect(calls, 0, reason: 'still more than one screen before the end');

    await tester.drag(find.byType(ListView), const Offset(0, -1500));
    await tester.pumpAndSettle();
    expect(calls, greaterThan(0));
  });

  testWidgets('a list shorter than the screen asks for more at once', (
    tester,
  ) async {
    var calls = 0;
    await pump(tester, 3, () => calls++);

    expect(calls, greaterThan(0));
  });
}
