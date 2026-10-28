import 'package:flutter/material.dart';

import '../motion/nordic_motion.dart';

/// Press feedback for anything tappable (MOB-UI-12, `apple-design` skill §1): the child shrinks
/// to [pressedScale] the moment the finger touches down, not on release, and springs back from
/// wherever it is when the finger lifts, so a quick tap never waits for an animation to end.
/// Under reduced motion it dims instead of scaling. It only shows feedback; the child keeps
/// handling the tap itself.
class Pressable extends StatefulWidget {
  final Widget child;
  final bool enabled;
  final double pressedScale;

  const Pressable({
    super.key,
    required this.child,
    this.enabled = true,
    this.pressedScale = 0.97,
  });

  @override
  State<Pressable> createState() => _PressableState();
}

class _PressableState extends State<Pressable>
    with SingleTickerProviderStateMixin {
  late final AnimationController _scale = AnimationController.unbounded(
    vsync: this,
    value: 1.0,
  );
  bool _dimmed = false;

  @override
  void dispose() {
    _scale.dispose();
    super.dispose();
  }

  void _down(PointerDownEvent _) {
    if (!widget.enabled) return;
    if (NordicMotion.reduced(context)) {
      setState(() => _dimmed = true);
      return;
    }
    _scale.animateTo(
      widget.pressedScale,
      duration: NordicMotion.press,
      curve: Curves.easeOut,
    );
  }

  void _up(PointerEvent _) {
    if (_dimmed) setState(() => _dimmed = false);
    if (_scale.value == 1.0 && !_scale.isAnimating) return;
    // From the current scale and speed, so a release mid-press does not jump.
    _scale.animateWith(
      NordicMotion.standard.simulate(
        from: _scale.value,
        to: 1.0,
        velocity: _scale.velocity,
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Listener(
      behavior: HitTestBehavior.translucent,
      onPointerDown: _down,
      onPointerUp: _up,
      onPointerCancel: _up,
      child: AnimatedBuilder(
        animation: _scale,
        builder: (context, child) => Opacity(
          opacity: _dimmed ? 0.7 : 1.0,
          child: Transform.scale(scale: _scale.value, child: child),
        ),
        child: widget.child,
      ),
    );
  }
}
