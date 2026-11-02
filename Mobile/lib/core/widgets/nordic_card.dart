import 'package:flutter/material.dart';

import '../theme/nordic_colors.dart';
import '../theme/nordic_elevation.dart';
import '../theme/nordic_radius.dart';
import '../theme/nordic_spacing.dart';
import 'pressable.dart';

/// The white card of TỔ ẤM (`Mobile/DESIGN-SYSTEM.md` §4): a 1 px hairline, [NordicRadius.card]
/// corners and the soft [NordicElevation.card] shadow (redesign 2026-10-30; [elevated] false keeps
/// it flat, e.g. inside another card). [isHighlighted] draws an accent border instead of the hairline.
/// With [onTap] it is a button: it shrinks on press ([Pressable]) and shows a ripple.
class NordicCard extends StatelessWidget {
  final Widget child;
  final EdgeInsetsGeometry padding;
  final bool isHighlighted;
  final VoidCallback? onTap;
  final bool elevated;

  const NordicCard({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(NordicSpace.md),
    this.isHighlighted = false,
    this.onTap,
    this.elevated = true,
  });

  @override
  Widget build(BuildContext context) {
    final card = Material(
      color: NordicColors.surface,
      shape: RoundedRectangleBorder(
        borderRadius: NordicRadius.cardAll,
        side: BorderSide(
          color: isHighlighted
              ? NordicColors.primaryContainer
              : NordicColors.border,
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
    final lifted = elevated
        ? DecoratedBox(
            decoration: const BoxDecoration(
              borderRadius: NordicRadius.cardAll,
              boxShadow: NordicElevation.card,
            ),
            child: card,
          )
        : card;
    return onTap == null ? lifted : Pressable(child: lifted);
  }
}
