import 'package:flutter/material.dart';

import '../theme/nordic_colors.dart';
import '../theme/nordic_radius.dart';
import '../theme/nordic_spacing.dart';
import 'pressable.dart';

/// The white card of TỔ ẤM (MOB-UI-13, `Mobile/DESIGN-SYSTEM.md` §4): flat, a 1 px hairline,
/// [NordicRadius.card] corners. [isHighlighted] draws a primary border instead of the hairline.
/// With [onTap] it is a button: it shrinks on press ([Pressable]) and shows a ripple.
class NordicCard extends StatelessWidget {
  final Widget child;
  final EdgeInsetsGeometry padding;
  final bool isHighlighted;
  final VoidCallback? onTap;

  const NordicCard({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(NordicSpace.md),
    this.isHighlighted = false,
    this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    final card = Material(
      color: NordicColors.surface,
      shape: RoundedRectangleBorder(
        borderRadius: NordicRadius.cardAll,
        side: BorderSide(
          color: isHighlighted ? NordicColors.primary : NordicColors.border,
          width: isHighlighted ? 2.0 : 1.0,
        ),
      ),
      clipBehavior: Clip.antiAlias,
      child: onTap == null
          ? Padding(padding: padding, child: child)
          : InkWell(
              onTap: onTap,
              child: Padding(padding: padding, child: child),
            ),
    );
    return onTap == null ? card : Pressable(child: card);
  }
}
