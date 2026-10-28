import 'package:flutter/material.dart';

import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';

/// Interactive card explaining and displaying S_total = S_sàn x N_tầng calculation
/// according to PRD §1.1 and .spec/contracts/customers.md §2.2.
class AreaCalculatorCard extends StatelessWidget {
  final double floorAreaM2;
  final int numFloors;
  final double totalAreaM2;

  const AreaCalculatorCard({
    super.key,
    required this.floorAreaM2,
    required this.numFloors,
    required this.totalAreaM2,
  });

  @override
  Widget build(BuildContext context) {
    // ui-templates `customer/address-form.html` "Diện tích": floor × floors = total, the total as the big number.
    const muted = TextStyle(fontSize: 14.0, color: NordicColors.textSecondary);
    return Container(
      padding: const EdgeInsets.all(16.0),
      decoration: BoxDecoration(
        color: NordicColors.surface,
        borderRadius: BorderRadius.zero,
        border: Border.all(color: NordicColors.border),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.end,
        children: [
          Expanded(
            child: Wrap(
              spacing: 8.0,
              crossAxisAlignment: WrapCrossAlignment.end,
              children: [
                Text(
                  '${floorAreaM2.toStringAsFixed(1)} m²',
                  style: NordicTypography.titleSmall,
                ),
                const Text('×', style: muted),
                Text('$numFloors tầng', style: NordicTypography.titleSmall),
                const Text('=', style: muted),
              ],
            ),
          ),
          Text(
            '${totalAreaM2.toStringAsFixed(1)} m²',
            key: const Key('area-total'),
            style: NordicTypography.numberLarge,
          ),
        ],
      ),
    );
  }
}
