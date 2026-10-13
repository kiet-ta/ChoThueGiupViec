import 'package:flutter/widgets.dart';
import 'package:flutter_test/flutter_test.dart';

/// Lays the screen out on the 390x844 design frame (Mobile/ARCHITECTURE.md) instead of the
/// default 800x600 test surface, and restores the surface after the test.
void useDesignFrame(WidgetTester tester) {
  tester.view.physicalSize = const Size(390, 844);
  tester.view.devicePixelRatio = 1.0;
  addTearDown(() {
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
}

/// Scrolls the screen (its outermost list) until [finder] is on screen, like a user would.
/// The screens use a lazy ListView, so a widget off screen is not built until it is scrolled to;
/// scrolling starts from the top so a widget above the current position is reached too.
Future<void> scrollTo(WidgetTester tester, Finder finder) async {
  if (finder.evaluate().isEmpty) {
    final scrollable = find.byType(Scrollable).first;
    await tester.drag(scrollable, const Offset(0.0, 10000.0));
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(finder, 100.0, scrollable: scrollable);
  } else {
    await tester.ensureVisible(finder);
  }
  await tester.pumpAndSettle();
}

/// Scrolls [finder] into view, then taps it.
Future<void> tapVisible(WidgetTester tester, Finder finder) async {
  await scrollTo(tester, finder);
  await tester.tap(finder);
  await tester.pumpAndSettle();
}
