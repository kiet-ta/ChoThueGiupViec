import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../theme/nordic_colors.dart';

/// ui-templates ring SVG: a circular progress with a centred label (time left, steps done).
class ProgressRing extends StatelessWidget {
  /// 0..1.
  final double value;
  final double size;
  final double stroke;
  final Color color;
  final Widget? child;

  const ProgressRing({
    super.key,
    required this.value,
    this.size = 120,
    this.stroke = 10,
    this.color = NordicColors.primary,
    this.child,
  });

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: size,
      height: size,
      child: CustomPaint(
        painter: _RingPainter(value.clamp(0.0, 1.0), stroke, color),
        child: Center(child: child),
      ),
    );
  }
}

class _RingPainter extends CustomPainter {
  final double value;
  final double stroke;
  final Color color;

  _RingPainter(this.value, this.stroke, this.color);

  @override
  void paint(Canvas canvas, Size size) {
    final rect = Offset.zero & size;
    final arc = rect.deflate(stroke / 2);
    final track = Paint()
      ..color = NordicColors.surfaceContainer
      ..style = PaintingStyle.stroke
      ..strokeWidth = stroke;
    final bar = Paint()
      ..color = color
      ..style = PaintingStyle.stroke
      ..strokeWidth = stroke
      ..strokeCap = StrokeCap.round;
    canvas.drawArc(arc, 0, 2 * math.pi, false, track);
    if (value > 0) {
      canvas.drawArc(arc, -math.pi / 2, 2 * math.pi * value, false, bar);
    }
  }

  @override
  bool shouldRepaint(_RingPainter old) =>
      old.value != value || old.color != color || old.stroke != stroke;
}
