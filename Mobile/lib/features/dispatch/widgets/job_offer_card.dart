import 'package:flutter/material.dart';

import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/countdown_timer_widget.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../models/job_offer.dart';

/// Card component showing job offer details and 30-second countdown action buttons.
/// MOB-UI-21 (`Mobile/DESIGN-SYSTEM-WORKER.md`): the net amount and the countdown are the big numbers; tokens only.
class JobOfferCard extends StatelessWidget {
  final JobOffer offer;
  final VoidCallback onAccept;
  final VoidCallback onDecline;
  final VoidCallback onTimeout;
  final bool isAccepting;
  final bool isDeclining;

  const JobOfferCard({
    super.key,
    required this.offer,
    required this.onAccept,
    required this.onDecline,
    required this.onTimeout,
    this.isAccepting = false,
    this.isDeclining = false,
  });

  String _formatCurrency(double amount) {
    final int intAmount = amount.round();
    final buffer = StringBuffer();
    final str = intAmount.toString();
    for (int i = 0; i < str.length; i++) {
      if (i > 0 && (str.length - i) % 3 == 0) {
        buffer.write('.');
      }
      buffer.write(str[i]);
    }
    return '${buffer.toString()} đ';
  }

  static const int _consecutiveWindowSeconds = 120;
  int get _windowSeconds =>
      offer.isConsecutiveShift ? _consecutiveWindowSeconds : 30;

  String get _tierLabel =>
      offer.serviceTier == 'PREMIUM' ? 'Cao cấp' : 'Cơ bản';

  @override
  Widget build(BuildContext context) {
    final commission = offer.grossAmount - offer.netEarnings;
    final netFlex =
        (offer.netEarnings /
                (offer.grossAmount > 0 ? offer.grossAmount : 1) *
                100)
            .round()
            .clamp(1, 99);
    return NordicCard(
      padding: const EdgeInsets.all(NordicSpace.gutter),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          // Header: caption + booking date, countdown ring (30 s, or 120 s for the second consecutive shift)
          Row(
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      offer.isConsecutiveShift
                          ? 'Ca thứ hai liên tiếp'
                          : 'Việc mới',
                      style: NordicTypography.bodySmall.copyWith(
                        color: NordicColors.textSecondary,
                      ),
                    ),
                    const SizedBox(height: NordicSpace.xxs),
                    Text(offer.bookingDate, style: NordicTypography.h2),
                  ],
                ),
              ),
              CountdownTimerWidget(
                totalSeconds: _windowSeconds,
                initialSeconds: offer.remainingSeconds > 0
                    ? offer.remainingSeconds
                    : _windowSeconds,
                onTimeout: onTimeout,
              ),
            ],
          ),
          if (offer.isConsecutiveShift) ...[
            const SizedBox(height: NordicSpace.xs),
            Text(
              'Đơn này thiếu một thợ. Gia chủ đồng ý để bạn làm luôn ca ngay sau ca bạn đã nhận. Bạn có $_consecutiveWindowSeconds giây để xác nhận.',
              key: const Key('consecutive_shift_notice'),
              style: NordicTypography.bodySmall,
            ),
          ],
          const SizedBox(height: NordicSpace.md),

          // Net amount (thực nhận sau phí) with the gross / commission split
          Container(
            padding: const EdgeInsets.all(NordicSpace.gutter),
            decoration: const BoxDecoration(
              color: NordicColors.surfaceLow,
              borderRadius: NordicRadius.fieldAll,
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'Thực nhận',
                  style: NordicTypography.bodySmall.copyWith(
                    color: NordicColors.textSecondary,
                  ),
                ),
                const SizedBox(height: NordicSpace.xs),
                Text(
                  _formatCurrency(offer.netEarnings),
                  style: NordicTypography.numberLarge,
                ),
                const SizedBox(height: NordicSpace.md),
                Row(
                  children: [
                    Expanded(
                      flex: netFlex,
                      child: Container(
                        height: NordicSpace.xs,
                        decoration: const BoxDecoration(
                          color: NordicColors.primary,
                          borderRadius: NordicRadius.pill,
                        ),
                      ),
                    ),
                    const SizedBox(width: NordicSpace.xxs),
                    Expanded(
                      flex: 100 - netFlex,
                      child: Container(
                        height: NordicSpace.xs,
                        decoration: const BoxDecoration(
                          color: NordicColors.accentAmber,
                          borderRadius: NordicRadius.pill,
                        ),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: NordicSpace.xs),
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    Flexible(
                      child: _splitLabel(
                        'Giá ca',
                        _formatCurrency(offer.grossAmount),
                      ),
                    ),
                    const SizedBox(width: NordicSpace.xs),
                    Flexible(
                      child: _splitLabel(
                        'Phí ${(offer.commissionRate * 100).round()}%',
                        _formatCurrency(commission),
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),
          const SizedBox(height: NordicSpace.sm),

          // Three info cards: start time, distance, service tier
          Row(
            children: [
              Expanded(
                child: _buildDetailItem(
                  icon: Icons.schedule_outlined,
                  value: offer.shiftTime.split('-').first,
                  caption:
                      '${offer.estimatedDurationHours.toStringAsFixed(offer.estimatedDurationHours % 1 == 0 ? 0 : 1)} giờ',
                ),
              ),
              const SizedBox(width: NordicSpace.xs),
              Expanded(
                child: _buildDetailItem(
                  icon: Icons.near_me_outlined,
                  value: '${offer.approximateDistanceKm.toStringAsFixed(1)} km',
                  caption: offer.district,
                ),
              ),
              const SizedBox(width: NordicSpace.xs),
              Expanded(
                child: _buildDetailItem(
                  icon: Icons.cleaning_services_outlined,
                  value: _tierLabel,
                  caption: offer.shiftTime,
                ),
              ),
            ],
          ),
          const SizedBox(height: NordicSpace.gutter),

          // Actions: Decline (secondary) / Accept
          Row(
            children: [
              Expanded(
                child: NordicButton(
                  key: const Key('decline_offer_button'),
                  label: 'Bỏ qua',
                  variant: NordicButtonVariant.secondary,
                  isLoading: isDeclining,
                  onPressed: isAccepting ? null : onDecline,
                ),
              ),
              const SizedBox(width: NordicSpace.sm),
              Expanded(
                flex: 2,
                child: NordicButton(
                  key: const Key('accept_offer_button'),
                  label: offer.isConsecutiveShift
                      ? 'Xác nhận ca thứ hai (${_windowSeconds}s)'
                      : 'Nhận việc',
                  variant: NordicButtonVariant.primary,
                  isLoading: isAccepting,
                  onPressed: isDeclining ? null : onAccept,
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }

  Widget _splitLabel(String label, String value) {
    return Text.rich(
      TextSpan(
        text: '$label ',
        style: NordicTypography.bodySmall.copyWith(
          color: NordicColors.textSecondary,
        ),
        children: [
          TextSpan(
            text: value,
            style: NordicTypography.bodySmall.copyWith(
              color: NordicColors.textTitle,
              fontWeight: FontWeight.w700,
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildDetailItem({
    required IconData icon,
    required String value,
    required String caption,
  }) {
    return Container(
      padding: const EdgeInsets.symmetric(
        vertical: NordicSpace.sm,
        horizontal: NordicSpace.xs,
      ),
      decoration: BoxDecoration(
        color: NordicColors.surface,
        borderRadius: NordicRadius.fieldAll,
        border: Border.all(color: NordicColors.border),
      ),
      child: Column(
        children: [
          Icon(icon, size: 20.0, color: NordicColors.primary),
          const SizedBox(height: NordicSpace.xxs),
          Text(
            value,
            style: NordicTypography.titleSmall,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
          ),
          Text(
            caption,
            style: NordicTypography.labelSmall.copyWith(
              color: NordicColors.textSecondary,
            ),
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
          ),
        ],
      ),
    );
  }
}
