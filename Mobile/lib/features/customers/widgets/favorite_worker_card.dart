import 'package:flutter/material.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../models/favorite_worker.dart';

/// Card presenting a saved favorite worker in TỔ ẤM Nordic Care design style.
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
    return NordicCard(
      padding: const EdgeInsets.all(16.0),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // Header: Avatar, Name & Work Status
          Row(
            children: [
              Container(
                width: 44.0,
                height: 44.0,
                decoration: BoxDecoration(
                  color: NordicColors.primary.withValues(alpha: 0.1),
                  shape: BoxShape.circle,
                ),
                child: const Icon(
                  Icons.person_rounded,
                  color: NordicColors.primary,
                  size: 24.0,
                ),
              ),
              const SizedBox(width: 12.0),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      worker.fullName,
                      style: NordicTypography.h3,
                      overflow: TextOverflow.ellipsis,
                    ),
                    const SizedBox(height: 2.0),
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 6.0, vertical: 2.0),
                      decoration: BoxDecoration(
                        color: worker.statusColor.withValues(alpha: 0.1),
                        borderRadius: BorderRadius.circular(6.0),
                      ),
                      child: Text(
                        worker.statusDisplayName,
                        style: NordicTypography.labelSmall.copyWith(
                          fontSize: 10.0,
                          color: worker.statusColor,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                    ),
                  ],
                ),
              ),
              IconButton(
                icon: const Icon(Icons.favorite_rounded, color: NordicColors.error),
                tooltip: 'Bỏ yêu thích',
                onPressed: onRemove,
              ),
            ],
          ),
          const SizedBox(height: 12.0),

          // Metrics strip: Rating & Completed Jobs
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 10.0, vertical: 8.0),
            decoration: BoxDecoration(
              color: NordicColors.surfaceSubtle,
              borderRadius: BorderRadius.circular(8.0),
              border: Border.all(color: NordicColors.border),
            ),
            child: Row(
              children: [
                const Icon(
                  Icons.star_rounded,
                  color: NordicColors.accentAmber,
                  size: 16.0,
                ),
                const SizedBox(width: 4.0),
                Text(
                  worker.ratingAvg.toStringAsFixed(1),
                  style: NordicTypography.bodyRegular.copyWith(
                    fontWeight: FontWeight.w700,
                    color: NordicColors.textTitle,
                  ),
                ),
                const SizedBox(width: 8.0),
                Container(
                  width: 1.0,
                  height: 14.0,
                  color: NordicColors.borderVariant,
                ),
                const SizedBox(width: 8.0),
                Expanded(
                  child: Text(
                    '${worker.completedJobs} ca hoàn thành',
                    style: NordicTypography.bodySmall.copyWith(
                      fontWeight: FontWeight.w600,
                    ),
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: 12.0),

          // Action CTA button
          SizedBox(
            width: double.infinity,
            child: NordicButton(
              label: 'Đặt Ca Với Thợ',
              icon: const Icon(Icons.calendar_today_rounded, size: 16.0),
              variant: NordicButtonVariant.primary,
              onPressed: onBookJob,
            ),
          ),
        ],
      ),
    );
  }
}
