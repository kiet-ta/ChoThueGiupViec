import 'package:flutter/material.dart';

import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/countdown_timer_widget.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../models/job_offer.dart';

/// Card component showing job offer details and 30-second countdown action buttons.
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
      offer.serviceTier == 'PREMIUM' ? 'Premium' : 'Tiêu chuẩn';

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
      padding: const EdgeInsets.all(20.0),
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
                          ? 'CA THỨ HAI LIÊN TIẾP'
                          : 'Việc mới',
                      style: NordicTypography.caps,
                    ),
                    const SizedBox(height: 4.0),
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
            const SizedBox(height: 8.0),
            Text(
              'Đơn này thiếu một thợ. Khách đồng ý để bạn làm luôn ca ngay sau ca bạn đã nhận. Bạn có $_consecutiveWindowSeconds giây để xác nhận.',
              key: const Key('consecutive_shift_notice'),
              style: NordicTypography.bodySmall,
            ),
          ],
          const SizedBox(height: 16.0),

          // Net amount (thực nhận sau hoa hồng) with the gross / commission split
          Container(
            padding: const EdgeInsets.all(20.0),
            decoration: BoxDecoration(
              color: NordicColors.surfaceLow,
              borderRadius: BorderRadius.circular(16.0),
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
                const SizedBox(height: 8.0),
                Text(
                  _formatCurrency(offer.netEarnings),
                  style: NordicTypography.numberLarge,
                ),
                const SizedBox(height: 16.0),
                Row(
                  children: [
                    Expanded(
                      flex: netFlex,
                      child: Container(
                        height: 8.0,
                        decoration: const BoxDecoration(
                          color: NordicColors.primary,
                          borderRadius: BorderRadius.horizontal(
                            left: Radius.circular(4.0),
                          ),
                        ),
                      ),
                    ),
                    const SizedBox(width: 2.0),
                    Expanded(
                      flex: 100 - netFlex,
                      child: Container(
                        height: 8.0,
                        decoration: const BoxDecoration(
                          color: NordicColors.accentAmber,
                          borderRadius: BorderRadius.horizontal(
                            right: Radius.circular(4.0),
                          ),
                        ),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 8.0),
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    Flexible(
                      child: _splitLabel(
                        'Giá ca',
                        _formatCurrency(offer.grossAmount),
                      ),
                    ),
                    const SizedBox(width: 8.0),
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
          const SizedBox(height: 12.0),

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
              const SizedBox(width: 8.0),
              Expanded(
                child: _buildDetailItem(
                  icon: Icons.near_me_outlined,
                  value: '${offer.approximateDistanceKm.toStringAsFixed(1)} km',
                  caption: offer.district,
                ),
              ),
              const SizedBox(width: 8.0),
              Expanded(
                child: _buildDetailItem(
                  icon: Icons.cleaning_services_outlined,
                  value: _tierLabel,
                  caption: offer.shiftTime,
                ),
              ),
            ],
          ),
          const SizedBox(height: 20.0),

          // Actions: Decline (ghost) / Accept
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
              const SizedBox(width: 10.0),
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
      padding: const EdgeInsets.symmetric(vertical: 12.0, horizontal: 6.0),
      decoration: BoxDecoration(
        color: NordicColors.surface,
        borderRadius: BorderRadius.circular(14.0),
        border: Border.all(color: NordicColors.border),
      ),
      child: Column(
        children: [
          Icon(icon, size: 20.0, color: NordicColors.primary),
          const SizedBox(height: 4.0),
          Text(
            value,
            style: NordicTypography.h3,
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
