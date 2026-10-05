import 'package:flutter/material.dart';
import '../theme/nordic_colors.dart';
import '../theme/nordic_typography.dart';

/// Top eyebrow pill badge with accent dot and uppercase text.
class EyebrowBadge extends StatelessWidget {
  final String text;

  const EyebrowBadge({
    super.key,
    required this.text,
  });

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12.0, vertical: 5.0),
      decoration: BoxDecoration(
        color: NordicColors.secondaryAccent.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(9999.0),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
            width: 6.0,
            height: 6.0,
            decoration: const BoxDecoration(
              color: NordicColors.primary,
              shape: BoxShape.circle,
            ),
          ),
          const SizedBox(width: 6.0),
          Text(
            text.toUpperCase(),
            style: NordicTypography.labelSmall,
          ),
        ],
      ),
    );
  }
}
