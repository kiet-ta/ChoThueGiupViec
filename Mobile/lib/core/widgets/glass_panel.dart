import 'package:flutter/material.dart';

import '../theme/nordic_colors.dart';
import '../theme/nordic_radius.dart';
import '../theme/nordic_spacing.dart';
import 'pressable.dart';

/// A translucent panel on a dark surface (`Mobile/DESIGN-SYSTEM.md` §4): white at 8 % with a white 14 % border,
/// no shadow. With [onTap] it is a button (shrinks on press, ripple).
class GlassPanel extends StatelessWidget {
  final Widget child;
  final EdgeInsetsGeometry padding;
  final VoidCallback? onTap;
  final bool pill;

  const GlassPanel({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(NordicSpace.md),
    this.onTap,
    this.pill = false,
  });

  /// The decoration alone, for a container that is not tappable.
  static BoxDecoration decoration({bool pill = false}) => BoxDecoration(
    color: NordicColors.textInverse.withValues(alpha: 0.08),
    borderRadius: pill ? NordicRadius.pill : NordicRadius.cardAll,
    border: Border.all(color: NordicColors.textInverse.withValues(alpha: 0.14)),
  );

  @override
  Widget build(BuildContext context) {
    final side = BorderSide(
      color: NordicColors.textInverse.withValues(alpha: 0.14),
    );
    final ShapeBorder shape = pill
        ? StadiumBorder(side: side)
        : RoundedRectangleBorder(
            borderRadius: NordicRadius.cardAll,
            side: side,
          );
    final panel = Material(
      color: NordicColors.textInverse.withValues(alpha: 0.08),
      shape: shape,
      clipBehavior: Clip.antiAlias,
      child: onTap == null
          ? Padding(padding: padding, child: child)
          : InkWell(
              onTap: onTap,
              child: Padding(padding: padding, child: child),
            ),
    );
    return onTap == null ? panel : Pressable(child: panel);
  }
}
