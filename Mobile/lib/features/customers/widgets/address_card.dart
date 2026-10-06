import 'package:flutter/material.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/trust_badge.dart';
import '../models/customer_address.dart';

/// Card component presenting a customer's saved address in the address book.
/// Highlights default address using double-bezel styling and TrustBadge.
class AddressCard extends StatelessWidget {
  final CustomerAddress address;
  final VoidCallback onEdit;
  final VoidCallback onDelete;

  const AddressCard({
    super.key,
    required this.address,
    required this.onEdit,
    required this.onDelete,
  });

  IconData _getHousingIcon() {
    switch (address.housingType) {
      case HousingType.apartment:
        return Icons.apartment_rounded;
      case HousingType.house:
        return Icons.home_rounded;
      case HousingType.room:
        return Icons.meeting_room_rounded;
    }
  }

  @override
  Widget build(BuildContext context) {
    return NordicCard(
      isHighlighted: address.isDefault,
      padding: const EdgeInsets.all(16.0),
      onTap: onEdit,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // Header: Label & Default Badge
          Row(
            children: [
              Container(
                width: 32.0,
                height: 32.0,
                decoration: BoxDecoration(
                  color: address.isDefault
                      ? NordicColors.primary.withValues(alpha: 0.1)
                      : NordicColors.surfaceSubtle,
                  borderRadius: BorderRadius.circular(8.0),
                ),
                child: Icon(
                  _getHousingIcon(),
                  color: address.isDefault ? NordicColors.primary : NordicColors.secondaryAccent,
                  size: 18.0,
                ),
              ),
              const SizedBox(width: 10.0),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      address.label,
                      style: NordicTypography.h3,
                      overflow: TextOverflow.ellipsis,
                    ),
                    Text(
                      '${address.housingType.displayName} • ${address.numFloors} tầng',
                      style: NordicTypography.bodySmall.copyWith(
                        color: NordicColors.secondaryAccent,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ],
                ),
              ),
              if (address.isDefault)
                const TrustBadge(text: 'MẶC ĐỊNH'),
            ],
          ),
          const SizedBox(height: 12.0),

          // Address Line
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Icon(
                Icons.place_outlined,
                size: 16.0,
                color: NordicColors.textSecondary,
              ),
              const SizedBox(width: 6.0),
              Expanded(
                child: Text(
                  address.fullAddress,
                  style: NordicTypography.bodyRegular.copyWith(
                    color: NordicColors.textBody,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 12.0),

          // S_total metric banner
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 10.0, vertical: 8.0),
            decoration: BoxDecoration(
              color: NordicColors.surfaceSubtle,
              borderRadius: BorderRadius.circular(8.0),
              border: Border.all(color: NordicColors.border),
            ),
            child: Row(
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        'Tổng diện tích (S_total):',
                        style: NordicTypography.labelSmall.copyWith(fontSize: 10.0),
                      ),
                      Text(
                        '${address.totalAreaM2.toStringAsFixed(1)} m²',
                        style: NordicTypography.bodyRegular.copyWith(
                          color: NordicColors.primary,
                          fontWeight: FontWeight.w800,
                        ),
                      ),
                    ],
                  ),
                ),
                Container(
                  width: 1.0,
                  height: 24.0,
                  color: NordicColors.border,
                ),
                const SizedBox(width: 10.0),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        'Diện tích sàn:',
                        style: NordicTypography.labelSmall.copyWith(fontSize: 10.0),
                      ),
                      Text(
                        '${address.floorAreaM2.toStringAsFixed(1)} m²',
                        style: NordicTypography.bodySmall.copyWith(
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: 10.0),

          // Footer: GPS and Action buttons
          Row(
            children: [
              Icon(
                Icons.gps_fixed,
                size: 13.0,
                color: NordicColors.textSecondary.withValues(alpha: 0.7),
              ),
              const SizedBox(width: 4.0),
              Expanded(
                child: Text(
                  '${address.latitude.toStringAsFixed(4)}, ${address.longitude.toStringAsFixed(4)}',
                  style: NordicTypography.bodySmall.copyWith(
                    fontSize: 11.0,
                    color: NordicColors.textSecondary,
                  ),
                  overflow: TextOverflow.ellipsis,
                ),
              ),
              IconButton(
                icon: const Icon(Icons.edit_outlined, size: 18.0),
                color: NordicColors.primary,
                tooltip: 'Chỉnh sửa',
                padding: EdgeInsets.zero,
                constraints: const BoxConstraints(minWidth: 32, minHeight: 32),
                onPressed: onEdit,
              ),
              IconButton(
                icon: const Icon(Icons.delete_outline_rounded, size: 18.0),
                color: NordicColors.error,
                tooltip: 'Xoá',
                padding: EdgeInsets.zero,
                constraints: const BoxConstraints(minWidth: 32, minHeight: 32),
                onPressed: onDelete,
              ),
            ],
          ),
        ],
      ),
    );
  }
}
