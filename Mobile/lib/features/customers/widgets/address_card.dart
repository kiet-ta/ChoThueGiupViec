import 'package:flutter/material.dart';

import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/status_pill.dart';
import '../models/customer_address.dart';

/// One row of the address book (ui-templates `customer/addresses.html`): housing icon, label with the
/// default pill, the address on one line, and the total area as the number on the right.
/// Each address is its own pressable [NordicCard] (MOB-UI-14); a tap edits, the bin deletes.
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
        return Icons.apartment_outlined;
      case HousingType.house:
        return Icons.home_outlined;
      case HousingType.room:
        return Icons.meeting_room_outlined;
    }
  }

  /// `58`, `57,5` (Vietnamese decimal comma).
  String get _area {
    final a = address.totalAreaM2;
    return a == a.roundToDouble()
        ? a.toStringAsFixed(0)
        : a.toStringAsFixed(1).replaceAll('.', ',');
  }

  @override
  Widget build(BuildContext context) {
    return NordicCard(
      onTap: onEdit,
      padding: const EdgeInsets.fromLTRB(
        NordicSpace.md,
        NordicSpace.sm,
        NordicSpace.xxs,
        NordicSpace.sm,
      ),
      child: Row(
        children: [
          Container(
            width: 40.0,
            height: 40.0,
            decoration: BoxDecoration(
              color: NordicColors.surfaceLow,
              borderRadius: NordicRadius.tileAll,
            ),
            child: Icon(_getHousingIcon(), color: NordicColors.primary),
          ),
          const SizedBox(width: 12.0),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Flexible(
                      child: Text(
                        address.label,
                        style: NordicTypography.titleSmall,
                        overflow: TextOverflow.ellipsis,
                      ),
                    ),
                    if (address.isDefault) ...[
                      const SizedBox(width: 6.0),
                      const StatusPill('Mặc định', tone: PillTone.ok),
                    ],
                  ],
                ),
                const SizedBox(height: 2.0),
                Text(
                  address.fullAddress,
                  style: NordicTypography.bodySmall,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
              ],
            ),
          ),
          const SizedBox(width: 8.0),
          Column(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              Text(
                _area,
                key: Key('address-area-${address.addressId}'),
                style: NordicTypography.titleSmall,
              ),
              const Text(
                'm²',
                style: TextStyle(
                  fontSize: 11.0,
                  color: NordicColors.textSecondary,
                ),
              ),
            ],
          ),
          IconButton(
            icon: const Icon(Icons.delete_outline, size: 20.0),
            color: NordicColors.textSecondary,
            tooltip: 'Xoá',
            onPressed: onDelete,
          ),
        ],
      ),
    );
  }
}
