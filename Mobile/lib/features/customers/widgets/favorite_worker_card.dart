import 'package:flutter/material.dart';

import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/avatar_initials.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/status_pill.dart';
import '../models/favorite_worker.dart';

/// A saved favourite worker (customer redesign v2 §4.3): who, the rating and finished shifts, a heart to drop them,
/// and under a hairline the one action: book with them (the form opens with them picked, booking.md 3.3).
class FavoriteWorkerCard extends StatelessWidget {
  final FavoriteWorker worker;
  final VoidCallback onBookJob;
  final VoidCallback onRemove;

  /// Rotates the avatar tint (amber, green, grey) from one card to the next.
  final int tintIndex;

  const FavoriteWorkerCard({
    super.key,
    required this.worker,
    required this.onBookJob,
    required this.onRemove,
    this.tintIndex = 0,
  });

  static const _tints = [
    (NordicColors.amberSoft, NordicColors.onAmberSoft),
    (NordicColors.primaryFixed, NordicColors.primary),
    (NordicColors.surfaceContainer, NordicColors.textTitle),
  ];

  /// The given name: the last word of the full name.
  static String shortName(String fullName) {
    final parts = fullName.trim().split(RegExp(r'\s+'));
    return parts.isEmpty || parts.last.isEmpty ? fullName : parts.last;
  }

  @override
  Widget build(BuildContext context) {
    final busy = worker.workStatus.toUpperCase() == 'BUSY';
    final (tint, ink) = _tints[tintIndex % _tints.length];
    final short = shortName(worker.fullName);
    return NordicCard(
      padding: const EdgeInsets.all(NordicSpace.md),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              AvatarInitials(
                worker.fullName,
                size: 52.0,
                background: tint,
                foreground: ink,
              ),
              const SizedBox(width: NordicSpace.sm),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      worker.fullName,
                      style: NordicTypography.titleSmall,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                    const SizedBox(height: 2.0),
                    Row(
                      children: [
                        const Icon(
                          Icons.star_rounded,
                          size: 16.0,
                          color: NordicColors.accentAmber,
                        ),
                        const SizedBox(width: 2.0),
                        Flexible(
                          child: Text(
                            '${worker.ratingAvg.toStringAsFixed(1).replaceAll('.', ',')} · ${worker.completedJobs} ca đã làm',
                            style: NordicTypography.bodySmall,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                          ),
                        ),
                      ],
                    ),
                    if (busy) ...[
                      const SizedBox(height: NordicSpace.xxs),
                      const StatusPill(
                        'Đang làm việc',
                        tone: PillTone.neutral,
                        dot: true,
                      ),
                    ],
                  ],
                ),
              ),
              IconButton(
                style: IconButton.styleFrom(
                  backgroundColor: NordicColors.primaryFixed,
                  fixedSize: const Size(44, 44),
                ),
                icon: const Icon(Icons.favorite, color: NordicColors.primary),
                tooltip: 'Bỏ yêu thích',
                onPressed: onRemove,
              ),
            ],
          ),
          const Divider(height: 28, color: NordicColors.surfaceContainer),
          Row(
            children: [
              Expanded(
                child: Text(
                  'Ngày rảnh của $short hiện ở bước chọn lịch',
                  style: NordicTypography.bodySmall,
                ),
              ),
              const SizedBox(width: NordicSpace.sm),
              NordicButton(label: 'Đặt với $short', onPressed: onBookJob),
            ],
          ),
        ],
      ),
    );
  }
}
