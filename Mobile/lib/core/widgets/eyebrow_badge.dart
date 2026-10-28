import 'package:flutter/material.dart';

import '../theme/nordic_colors.dart';
import '../theme/nordic_typography.dart';

/// Top eyebrow badge (square, MOB-UI-11) with accent dot and uppercase text.
class EyebrowBadge extends StatelessWidget {
  final String text;

  const EyebrowBadge({super.key, required this.text});

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12.0, vertical: 5.0),
      decoration: BoxDecoration(
        color: NordicColors.secondaryAccent.withValues(alpha: 0.12),
        borderRadius: BorderRadius.zero,
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
          Flexible(
            child: Text(
              text.toUpperCase(),
              style: NordicTypography.labelSmall,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
            ),
          ),
        ],
      ),
    );
  }
}
