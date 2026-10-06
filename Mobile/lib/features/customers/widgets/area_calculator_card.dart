import 'package:flutter/material.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/eyebrow_badge.dart';

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
    return Container(
      padding: const EdgeInsets.all(14.0),
      decoration: BoxDecoration(
        color: NordicColors.surfaceSubtle,
        borderRadius: BorderRadius.circular(12.0),
        border: Border.all(color: NordicColors.borderVariant),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Wrap(
            alignment: WrapAlignment.spaceBetween,
            crossAxisAlignment: WrapCrossAlignment.center,
            spacing: 8.0,
            runSpacing: 4.0,
            children: [
              const EyebrowBadge(text: 'CÔNG THỨC S_TOTAL'),
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 8.0, vertical: 2.0),
                decoration: BoxDecoration(
                  color: NordicColors.primary.withValues(alpha: 0.1),
                  borderRadius: BorderRadius.circular(6.0),
                ),
                child: Text(
                  'PRD §1.1',
                  style: NordicTypography.labelSmall.copyWith(
                    color: NordicColors.primary,
                    fontWeight: FontWeight.w700,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 10.0),
          Row(
            crossAxisAlignment: CrossAxisAlignment.baseline,
            textBaseline: TextBaseline.alphabetic,
            children: [
              Text(
                '${totalAreaM2.toStringAsFixed(1)} m²',
                style: NordicTypography.h2.copyWith(
                  color: NordicColors.primary,
                  fontWeight: FontWeight.w800,
                ),
              ),
              const SizedBox(width: 8.0),
              Expanded(
                child: Text(
                  'Tổng diện tích dọn dẹp',
                  style: NordicTypography.bodySmall,
                  overflow: TextOverflow.ellipsis,
                ),
              ),
            ],
          ),
          const SizedBox(height: 6.0),
          Container(
            padding: const EdgeInsets.all(8.0),
            decoration: BoxDecoration(
              color: NordicColors.surface,
              borderRadius: BorderRadius.circular(8.0),
              border: Border.all(color: NordicColors.border),
            ),
            child: Row(
              children: [
                const Icon(
                  Icons.calculate_outlined,
                  size: 16.0,
                  color: NordicColors.secondaryAccent,
                ),
                const SizedBox(width: 6.0),
                Expanded(
                  child: Text(
                    '${floorAreaM2.toStringAsFixed(1)} m² (sàn) × $numFloors tầng = ${totalAreaM2.toStringAsFixed(1)} m²',
                    style: NordicTypography.bodySmall.copyWith(
                      fontWeight: FontWeight.w600,
                      color: NordicColors.textTitle,
                    ),
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
