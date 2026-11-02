import 'package:flutter/material.dart';

import '../theme/nordic_colors.dart';
import '../theme/nordic_radius.dart';
import '../theme/nordic_typography.dart';
import 'pressable.dart';

/// ui-templates `.stat`: a KPI tile, one short label above one number. Flat card with
/// [NordicRadius.card] corners; shrinks on press when tappable (MOB-UI-13).
class StatTile extends StatelessWidget {
  final String label;
  final String value;
  final IconData? icon;

  /// Small text after the value (a unit such as `đ`).
  final String? unit;
  final VoidCallback? onTap;

  const StatTile({
    super.key,
    required this.label,
    required this.value,
    this.icon,
    this.unit,
    this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    final tile = Material(
      color: NordicColors.surface,
      shape: RoundedRectangleBorder(
        borderRadius: NordicRadius.cardAll,
        side: const BorderSide(color: NordicColors.border),
      ),
      child: InkWell(
        onTap: onTap,
        borderRadius: NordicRadius.cardAll,
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  if (icon != null) ...[
                    Icon(icon, size: 16, color: NordicColors.textSecondary),
                    const SizedBox(width: 6),
                  ],
                  Flexible(
                    child: Text(
                      label,
                      style: NordicTypography.bodySmall,
                      overflow: TextOverflow.ellipsis,
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 8),
              Text.rich(
                TextSpan(
                  text: value,
                  children: [
                    if (unit != null)
                      TextSpan(
                        text: ' $unit',
                        style: const TextStyle(fontSize: 14),
                      ),
                  ],
                ),
                style: NordicTypography.numberSmall,
              ),
            ],
          ),
        ),
      ),
    );
    return onTap == null ? tile : Pressable(child: tile);
  }
}
