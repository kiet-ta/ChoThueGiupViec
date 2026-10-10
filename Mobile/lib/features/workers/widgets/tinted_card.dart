import 'package:flutter/material.dart';
import '../../../core/theme/nordic_colors.dart';

/// A card of the workers screens with its own background colour (notices, highlighted rows).
/// Same shape and padding as `NordicCard`, which has no background parameter.
class TintedCard extends StatelessWidget {
  final Color backgroundColor;
  final Widget child;

  const TintedCard({super.key, required this.backgroundColor, required this.child});

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(16.0),
      decoration: BoxDecoration(
        color: backgroundColor,
        borderRadius: BorderRadius.circular(16.0),
        border: Border.all(color: NordicColors.border),
      ),
      child: child,
    );
  }
}
