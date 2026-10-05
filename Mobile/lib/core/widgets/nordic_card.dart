import 'package:flutter/material.dart';
import '../theme/nordic_colors.dart';

/// Double-bezel card structure specified in TỔ ẤM — Nordic Care.
class NordicCard extends StatelessWidget {
  final Widget child;
  final EdgeInsetsGeometry padding;
  final bool isHighlighted;
  final VoidCallback? onTap;

  const NordicCard({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(16.0),
    this.isHighlighted = false,
    this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    Widget cardContent = Container(
      padding: padding,
      decoration: BoxDecoration(
        color: NordicColors.surface,
        borderRadius: BorderRadius.circular(16.0),
        border: Border.all(
          color: isHighlighted ? NordicColors.primary : NordicColors.border,
          width: isHighlighted ? 2.0 : 1.0,
        ),
        boxShadow: isHighlighted
            ? [
                BoxShadow(
                  color: NordicColors.primary.withValues(alpha: 0.08),
                  blurRadius: 12.0,
                  offset: const Offset(0, 4),
                ),
              ]
            : [
                BoxShadow(
                  color: Colors.black.withValues(alpha: 0.02),
                  blurRadius: 6.0,
                  offset: const Offset(0, 2),
                ),
              ],
      ),
      child: child,
    );

    if (onTap != null) {
      return InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(16.0),
        child: cardContent,
      );
    }

    return cardContent;
  }
}
