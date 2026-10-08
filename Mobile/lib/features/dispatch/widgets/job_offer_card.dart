import 'package:flutter/material.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/countdown_timer_widget.dart';
import '../../../core/widgets/eyebrow_badge.dart';
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

  @override
  Widget build(BuildContext context) {
    return NordicCard(
      child: Padding(
        padding: const EdgeInsets.all(16.0),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            // Eyebrow badge
            Row(
              children: [
                EyebrowBadge(
                  text: offer.serviceTier == 'PREMIUM'
                      ? 'DỊCH VỤ PREMIUM'
                      : 'DỊCH VỤ TIÊU CHUẨN',
                ),
              ],
            ),
            const SizedBox(height: 12.0),

            // 30s Countdown timer banner
            Center(
              child: CountdownTimerWidget(
                totalSeconds: 30,
                initialSeconds: offer.remainingSeconds > 0 ? offer.remainingSeconds : 30,
                onTimeout: onTimeout,
              ),
            ),
            const SizedBox(height: 16.0),

            // District & Location
            Row(
              children: [
                const Icon(
                  Icons.location_on_outlined,
                  color: NordicColors.primary,
                  size: 24.0,
                ),
                const SizedBox(width: 8.0),
                Expanded(
                  child: Text(
                    offer.district,
                    style: NordicTypography.h2,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 4.0),
            Text(
              'Cách bạn khoảng ${offer.approximateDistanceKm.toStringAsFixed(1)} km',
              style: NordicTypography.bodySmall.copyWith(color: NordicColors.textSecondary),
            ),
            const SizedBox(height: 12.0),

            const Divider(color: NordicColors.border, height: 1.0),
            const SizedBox(height: 12.0),

            // Date & Shift duration
            Row(
              children: [
                Expanded(
                  child: _buildDetailItem(
                    icon: Icons.calendar_today_outlined,
                    label: 'Ngày làm',
                    value: offer.bookingDate,
                  ),
                ),
                Expanded(
                  child: _buildDetailItem(
                    icon: Icons.schedule_outlined,
                    label: 'Thời lượng',
                    value: '${offer.estimatedDurationHours.toStringAsFixed(1)}h (${offer.shiftTime})',
                  ),
                ),
              ],
            ),
            const SizedBox(height: 12.0),

            // Net earnings highlight (thực nhận sau hoa hồng)
            Container(
              padding: const EdgeInsets.all(14.0),
              decoration: BoxDecoration(
                color: NordicColors.primary.withAlpha(20),
                borderRadius: BorderRadius.circular(12.0),
                border: Border.all(color: NordicColors.primary.withAlpha(50)),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Row(
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    children: [
                      Expanded(
                        child: Text(
                          'THỰC NHẬN SAU HOA HỒNG',
                          style: NordicTypography.labelMedium.copyWith(fontSize: 12.0),
                        ),
                      ),
                      const SizedBox(width: 8.0),
                      Text(
                        'Hoa hồng ${(offer.commissionRate * 100).round()}%',
                        style: NordicTypography.bodySmall.copyWith(
                          color: NordicColors.textSecondary,
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 8.0),
                  Text(
                    _formatCurrency(offer.netEarnings),
                    style: NordicTypography.h1.copyWith(
                      color: NordicColors.primary,
                      fontWeight: FontWeight.bold,
                    ),
                  ),
                  const SizedBox(height: 4.0),
                  Text(
                    'Tổng tiền đơn hàng: ${_formatCurrency(offer.grossAmount)}',
                    style: NordicTypography.bodySmall.copyWith(
                      decoration: TextDecoration.lineThrough,
                      color: NordicColors.textSecondary,
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 16.0),

            // Actions: Accept / Decline
            Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                NordicButton(
                  key: const Key('accept_offer_button'),
                  label: 'Nhận Cuốc (30s)',
                  variant: NordicButtonVariant.primary,
                  isLoading: isAccepting,
                  onPressed: isDeclining ? null : onAccept,
                ),
                const SizedBox(height: 8.0),
                NordicButton(
                  key: const Key('decline_offer_button'),
                  label: 'Bỏ qua',
                  variant: NordicButtonVariant.secondary,
                  isLoading: isDeclining,
                  onPressed: isAccepting ? null : onDecline,
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildDetailItem({
    required IconData icon,
    required String label,
    required String value,
  }) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Icon(icon, size: 16.0, color: NordicColors.textSecondary),
            const SizedBox(width: 4.0),
            Text(
              label,
              style: NordicTypography.bodySmall.copyWith(color: NordicColors.textSecondary),
            ),
          ],
        ),
        const SizedBox(height: 4.0),
        Text(
          value,
          style: NordicTypography.labelMedium,
          overflow: TextOverflow.ellipsis,
        ),
      ],
    );
  }
}
