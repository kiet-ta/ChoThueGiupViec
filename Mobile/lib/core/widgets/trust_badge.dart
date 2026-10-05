import 'package:flutter/material.dart';
import '../theme/nordic_colors.dart';
import '../theme/nordic_typography.dart';

/// Pill badge with checkmark for core guarantees.
class TrustBadge extends StatelessWidget {
  final String text;

  const TrustBadge({
    super.key,
    required this.text,
  });

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10.0, vertical: 5.0),
      decoration: BoxDecoration(
        color: NordicColors.surface,
        borderRadius: BorderRadius.circular(9999.0),
        border: Border.all(color: NordicColors.border, width: 1.0),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withValues(alpha: 0.02),
            blurRadius: 4.0,
            offset: const Offset(0, 1),
          ),
        ],
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Icon(
            Icons.check_circle_rounded,
            size: 14.0,
            color: NordicColors.secondaryAccent,
          ),
          const SizedBox(width: 5.0),
          Flexible(
            child: Text(
              text,
              style: NordicTypography.bodySmall.copyWith(
                color: NordicColors.textTitle,
                fontWeight: FontWeight.w500,
              ),
              overflow: TextOverflow.ellipsis,
            ),
          ),
        ],
      ),
    );
  }
}
