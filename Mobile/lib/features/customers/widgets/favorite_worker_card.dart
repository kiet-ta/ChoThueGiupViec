import 'package:flutter/material.dart';

import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_spacing.dart';
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
    final status = worker.workStatus.toUpperCase();
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
                      status == 'BUSY'
                          ? 'Đang làm việc'
                          : 'Xem lịch khi đặt ca',
                      tone: PillTone.neutral,
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
          const SizedBox(height: NordicSpace.sm),
          Row(
            children: [
              const Icon(Icons.star, color: NordicColors.accentAmber),
              const SizedBox(width: NordicSpace.xxs),
              Expanded(
                child: Text(
                  '${worker.ratingAvg.toStringAsFixed(1).replaceAll('.', ',')} · ${worker.completedJobs} ca hoàn thành',
                  style: NordicTypography.bodyRegular,
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
}
