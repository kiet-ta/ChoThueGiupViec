import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/motion/error_shake.dart';
import 'package:mobile/core/motion/nordic_motion.dart';
import 'package:mobile/core/motion/nordic_page_transitions.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/core/widgets/nordic_button.dart';
import 'package:mobile/core/widgets/pressable.dart';

/// The app, with reduced motion on or off.
Widget _app(Widget child, {bool reduced = false}) => MaterialApp(
  builder: (context, page) => MediaQuery(
    data: MediaQuery.of(context).copyWith(disableAnimations: reduced),
    child: page!,
  ),
  home: Scaffold(body: Center(child: child)),
);

double _scaleOf(WidgetTester tester) => tester
    .widget<Transform>(
      find.descendant(
        of: find.byType(Pressable),
        matching: find.byType(Transform),
      ),
    )
    .transform
    .storage[0]; // x scale (getMaxScaleOnAxis would also count z = 1)

double _shakeOffset(WidgetTester tester) => tester
    .widget<Transform>(
      find.descendant(
        of: find.byType(ErrorShake),
        matching: find.byType(Transform),
      ),
    )
    .transform
    .getTranslation()
    .x;

/// Records the haptics sent to the platform.
List<String> _recordHaptics(WidgetTester tester) {
  final calls = <String>[];
  tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
    SystemChannels.platform,
    (call) async {
      if (call.method == 'HapticFeedback.vibrate') {
        calls.add('${call.arguments}');
      }
      return null;
    },
  );
  addTearDown(
    () => tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
      SystemChannels.platform,
      null,
    ),
  );
  return calls;
}

class _ShakeHost extends StatefulWidget {
  const _ShakeHost();

  @override
  State<_ShakeHost> createState() => _ShakeHostState();
}

class _ShakeHostState extends State<_ShakeHost> {
  int errors = 0;

  @override
  Widget build(BuildContext context) => Column(
    mainAxisSize: MainAxisSize.min,
    children: [
      ErrorShake(errors: errors, child: const Text('cells')),
      TextButton(
        onPressed: () => setState(() => errors++),
        child: const Text('fail'),
      ),
    ],
  );
}

void main() {
  group('NordicSpring', () {
    test('turns damping ratio + response into Flutter spring physics', () {
      final description = NordicMotion.standard.description;
      final omega = 2 * math.pi / 0.35;
      expect(description.mass, 1.0);
      expect(description.stiffness, closeTo(omega * omega, 1e-9));
      expect(description.damping, closeTo(2 * omega, 1e-9));
    });

    test('standard never overshoots and settles in about half a second', () {
      final curve = SpringCurve(NordicMotion.standard);
      var previous = 0.0;
      for (var i = 1; i < 100; i++) {
        final v = curve.transform(i / 100);
        expect(v, lessThanOrEqualTo(1.0));
        expect(v, greaterThanOrEqualTo(previous));
        previous = v;
      }
      expect(curve.transform(0.0), 0.0);
      expect(curve.transform(1.0), 1.0);
      final settle = NordicMotion.standard.settleDuration.inMilliseconds;
      expect(settle, inInclusiveRange(400, 700));
    });

    test('momentum overshoots a little; shake swings both ways', () {
      final momentum = SpringCurve(NordicMotion.momentum);
      final peak = [for (var i = 1; i < 100; i++) momentum.transform(i / 100)]
          .reduce(math.max);
      expect(peak, greaterThan(1.0));
      expect(peak, lessThan(1.05));

      final shake = NordicMotion.shake.simulate(to: 0.0, velocity: 520);
      final xs = [for (var t = 0.0; t < 0.4; t += 0.005) shake.x(t)];
      expect(xs.reduce(math.max), greaterThan(4.0));
      expect(xs.reduce(math.min), lessThan(-1.5));
      expect(shake.isDone(0.8), isTrue);
    });
  });

  group('Pressable', () {
    testWidgets('shrinks on pointer-down, before the finger lifts', (
      tester,
    ) async {
      await tester.pumpWidget(
        _app(
          Pressable(
            child: Container(width: 120, height: 48, color: Colors.green),
          ),
        ),
      );
      expect(_scaleOf(tester), 1.0);

      final gesture = await tester.startGesture(
        tester.getCenter(find.byType(Pressable)),
      );
      await tester.pump();
      await tester.pump(NordicMotion.press);
      expect(_scaleOf(tester), closeTo(0.97, 0.001));

      await gesture.up();
      await tester.pumpAndSettle();
      expect(_scaleOf(tester), closeTo(1.0, 0.001));
    });

    testWidgets('dims instead of scaling under reduced motion', (tester) async {
      await tester.pumpWidget(
        _app(
          Pressable(
            child: Container(width: 120, height: 48, color: Colors.green),
          ),
          reduced: true,
        ),
      );
      final gesture = await tester.startGesture(
        tester.getCenter(find.byType(Pressable)),
      );
      await tester.pump();
      await tester.pump(NordicMotion.press);
      expect(_scaleOf(tester), 1.0);
      expect(tester.widget<Opacity>(find.byType(Opacity)).opacity, 0.7);
      await gesture.up();
      await tester.pump();
      expect(tester.widget<Opacity>(find.byType(Opacity)).opacity, 1.0);
    });

    testWidgets('NordicButton gets press feedback, a disabled one does not', (
      tester,
    ) async {
      var taps = 0;
      await tester.pumpWidget(
        _app(NordicButton(label: 'Gửi', onPressed: () => taps++)),
      );
      final gesture = await tester.startGesture(
        tester.getCenter(find.text('Gửi')),
      );
      await tester.pump();
      await tester.pump(NordicMotion.press);
      expect(_scaleOf(tester), lessThan(1.0));
      await gesture.up();
      await tester.pumpAndSettle();
      expect(taps, 1);

      await tester.pumpAndSettle();
      await tester.pumpWidget(
        _app(const NordicButton(label: 'Tắt', onPressed: null)),
      );
      final again = await tester.startGesture(
        tester.getCenter(find.text('Tắt')),
      );
      await tester.pump();
      await tester.pump(NordicMotion.press);
      expect(_scaleOf(tester), closeTo(1.0, 0.001));
      await again.up();
    });
  });

  group('ErrorShake', () {
    testWidgets('shakes and vibrates on a new error, then settles', (
      tester,
    ) async {
      final haptics = _recordHaptics(tester);
      await tester.pumpWidget(_app(const _ShakeHost()));
      expect(_shakeOffset(tester), 0.0);

      await tester.tap(find.text('fail'));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 20));
      expect(_shakeOffset(tester).abs(), greaterThan(1.0));
      expect(haptics, ['HapticFeedbackType.heavyImpact']);

      await tester.pumpAndSettle();
      expect(_shakeOffset(tester).abs(), lessThan(0.01));
    });

    testWidgets('only vibrates under reduced motion', (tester) async {
      final haptics = _recordHaptics(tester);
      await tester.pumpWidget(_app(const _ShakeHost(), reduced: true));
      await tester.tap(find.text('fail'));
      await tester.pump(const Duration(milliseconds: 20));
      expect(_shakeOffset(tester), 0.0);
      expect(haptics, ['HapticFeedbackType.heavyImpact']);
    });
  });

  group('Page transitions', () {
    /// A themed app: a first page with a button that pushes a second page.
    Widget pages({bool reduced = false}) => MaterialApp(
      theme: NordicTheme.lightTheme,
      builder: (context, page) => MediaQuery(
        data: MediaQuery.of(context).copyWith(disableAnimations: reduced),
        child: page!,
      ),
      home: Builder(
        builder: (context) => Scaffold(
          body: Center(
            child: TextButton(
              onPressed: () => Navigator.of(context).push(
                MaterialPageRoute<void>(
                  builder: (_) =>
                      const Scaffold(body: Center(child: Text('second'))),
                ),
              ),
              child: const Text('first'),
            ),
          ),
        ),
      ),
    );

    testWidgets('the theme uses them on every platform', (tester) async {
      final builders = NordicTheme.lightTheme.pageTransitionsTheme.builders;
      for (final platform in TargetPlatform.values) {
        expect(builders[platform], isA<NordicPageTransitionsBuilder>());
      }
    });

    testWidgets('a new page slides in from the right, the page below moves '
        'back', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(tester.view.reset);
      await tester.pumpWidget(pages());

      await tester.tap(find.text('first'));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 150));
      expect(tester.getCenter(find.text('second')).dx, greaterThan(195 + 20));
      expect(tester.getCenter(find.text('first')).dx, lessThan(195));

      await tester.pumpAndSettle();
      expect(tester.getCenter(find.text('second')).dx, closeTo(195, 0.5));
    });

    testWidgets('dragging from the left edge takes the page back', (
      tester,
    ) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(tester.view.reset);
      await tester.pumpWidget(pages());
      await tester.tap(find.text('first'));
      await tester.pumpAndSettle();

      // Halfway: the page follows the finger.
      final drag = await tester.startGesture(const Offset(5, 400));
      await drag.moveBy(const Offset(20, 0));
      await drag.moveBy(const Offset(100, 0));
      await tester.pump();
      expect(tester.getCenter(find.text('second')).dx, greaterThan(195 + 90));

      await drag.moveBy(const Offset(120, 0));
      await drag.up();
      await tester.pumpAndSettle();
      expect(find.text('second'), findsNothing);
      expect(find.text('first'), findsOneWidget);
    });

    testWidgets('only fades under reduced motion', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(tester.view.reset);
      await tester.pumpWidget(pages(reduced: true));

      await tester.tap(find.text('first'));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 150));
      expect(tester.getCenter(find.text('second')).dx, closeTo(195, 0.5));
      expect(tester.getCenter(find.text('first')).dx, closeTo(195, 0.5));
      final fade = tester.widget<FadeTransition>(
        find
            .ancestor(
              of: find.text('second'),
              matching: find.byType(FadeTransition),
            )
            .first,
      );
      expect(fade.opacity.value, inExclusiveRange(0.0, 1.0));
      await tester.pumpAndSettle();
    });
  });
}
