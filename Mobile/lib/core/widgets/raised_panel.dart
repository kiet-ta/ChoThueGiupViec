import 'package:flutter/material.dart';

import '../theme/nordic_colors.dart';
import '../theme/nordic_elevation.dart';
import '../theme/nordic_radius.dart';
import '../theme/nordic_spacing.dart';

/// The one floating white panel of a screen (`Mobile/DESIGN-SYSTEM.md` §4): [NordicRadius.sheet] corners,
/// 20 px padding and the [NordicElevation.raised] shadow, with the side margin of the page. Place it inside a
/// `Transform.translate(Offset(0, -overlap))` so it rises over a [DarkHeader].
class RaisedPanel extends StatelessWidget {
  final Widget child;
  final EdgeInsetsGeometry padding;
  final EdgeInsetsGeometry margin;

  const RaisedPanel({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(20),
    this.margin = const EdgeInsets.symmetric(horizontal: NordicSpace.md),
  });

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: margin,
      child: DecoratedBox(
        decoration: const BoxDecoration(
          color: NordicColors.surface,
          borderRadius: BorderRadius.all(Radius.circular(NordicRadius.sheet)),
          boxShadow: NordicElevation.raised,
        ),
        child: Padding(padding: padding, child: child),
      ),
    );
  }
}
