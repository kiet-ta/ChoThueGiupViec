import 'package:flutter/material.dart';

import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/avatar_initials.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/status_pill.dart';
import '../models/favorite_worker.dart';

/// A saved favourite worker (ui-templates `customer/favorites.html`): who, whether they work now,
/// and two numbers, the rating with its stars and the finished shifts.
class FavoriteWorkerCard extends StatelessWidget {
  final FavoriteWorker worker;
  final VoidCallback onBookJob;
  final VoidCallback onRemove;

  const FavoriteWorkerCard({
    super.key,
    required this.worker,
    required this.onBookJob,
    required this.onRemove,
  });

  @override
  Widget build(BuildContext context) {
    final active = worker.workStatus.toUpperCase() == 'ACTIVE';
    return NordicCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              AvatarInitials(worker.fullName, size: 56.0),
              const SizedBox(width: 12.0),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      worker.fullName,
                      style: NordicTypography.titleMedium,
                      overflow: TextOverflow.ellipsis,
                    ),
                    const SizedBox(height: 4.0),
                    StatusPill(
                      worker.statusDisplayName,
                      tone: active ? PillTone.ok : PillTone.neutral,
                      dot: true,
                    ),
                  ],
                ),
              ),
              IconButton(
                icon: const Icon(Icons.favorite, color: NordicColors.primary),
                tooltip: 'Bỏ yêu thích',
                onPressed: onRemove,
              ),
            ],
          ),
          const SizedBox(height: 16.0),
          Row(
            children: [
              Expanded(
                child: _metric(
                  value: worker.ratingAvg
                      .toStringAsFixed(1)
                      .replaceAll('.', ','),
                  below: Row(
                    children: [
                      for (var i = 1; i <= 5; i++)
                        Icon(
                          i <= worker.ratingAvg.round()
                              ? Icons.star
                              : Icons.star_outline,
                          size: 16.0,
                          color: NordicColors.accentAmber,
                        ),
                    ],
                  ),
                ),
              ),
              const SizedBox(width: 12.0),
              Expanded(
                child: _metric(
                  value: '${worker.completedJobs}',
                  below: const Text(
                    'ca hoàn thành',
                    style: TextStyle(
                      fontSize: 11.0,
                      color: NordicColors.textSecondary,
                    ),
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 12.0),
          NordicButton(
            label: 'Đặt ca',
            width: double.infinity,
            variant: NordicButtonVariant.secondary,
            icon: const Icon(Icons.calendar_month_outlined, size: 18.0),
            onPressed: onBookJob,
          ),
        ],
      ),
    );
  }

  Widget _metric({required String value, required Widget below}) => Container(
    padding: const EdgeInsets.all(12.0),
    decoration: BoxDecoration(
      color: NordicColors.surfaceLow,
      borderRadius: NordicRadius.tileAll,
    ),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(value, style: NordicTypography.numberMedium),
        const SizedBox(height: 8.0),
        below,
      ],
    ),
  );
}
