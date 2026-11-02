import 'package:flutter/material.dart';

import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
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
        borderRadius: NordicRadius.cardAll,
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
                  '${floorAreaM2.toStringAsFixed(1).replaceAll('.', ',')} m²',
                  style: NordicTypography.titleSmall,
                ),
                const Text('×', style: muted),
                Text('$numFloors tầng', style: NordicTypography.titleSmall),
                const Text('=', style: muted),
              ],
            ),
          ),
          // The big total shrinks instead of pushing the row wider than the screen at a large text scale.
          Flexible(
            child: FittedBox(
              fit: BoxFit.scaleDown,
              alignment: Alignment.centerRight,
              child: Text(
                '${totalAreaM2.toStringAsFixed(1).replaceAll('.', ',')} m²',
                key: const Key('area-total'),
                maxLines: 1,
                style: NordicTypography.numberLarge,
              ),
            ),
          ),
        ],
      ),
    );
  }
}
