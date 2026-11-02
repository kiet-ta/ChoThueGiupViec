import 'package:flutter/services.dart';
import 'package:flutter/widgets.dart';

import 'nordic_motion.dart';

/// The "no" of a wrong entry (MOB-UI-12, `apple-design` skill §13): each time [errors] goes up,
/// the child shakes sideways on the [NordicMotion.shake] spring and the phone vibrates on the
/// same frame. Under reduced motion only the vibration plays. Pair it with a message that says
/// what is wrong: the shake only draws the eye.
class ErrorShake extends StatefulWidget {
  /// How many errors happened so far; raise it by one to shake once.
  final int errors;
  final Widget child;

  const ErrorShake({super.key, required this.errors, required this.child});

  @override
  State<ErrorShake> createState() => _ErrorShakeState();
}

class _ErrorShakeState extends State<ErrorShake>
    with SingleTickerProviderStateMixin {
  /// Initial sideways speed, px/s: about a 6 px first swing on the shake spring.
  static const double _kick = 520.0;

  late final AnimationController _offset = AnimationController.unbounded(
    vsync: this,
  );

  @override
  void didUpdateWidget(ErrorShake oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (widget.errors > oldWidget.errors) _shake();
  }

  void _shake() {
    HapticFeedback.heavyImpact();
    if (NordicMotion.reduced(context)) return;
    _offset.animateWith(
      NordicMotion.shake.simulate(
        from: _offset.value,
        to: 0.0,
        velocity: _offset.velocity + _kick,
      ),
    );
  }

  @override
  void dispose() {
    _offset.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: _offset,
      builder: (context, child) =>
          Transform.translate(offset: Offset(_offset.value, 0.0), child: child),
      child: widget.child,
    );
  }
}
