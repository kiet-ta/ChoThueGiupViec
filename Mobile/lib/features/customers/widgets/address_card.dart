import 'package:flutter/material.dart';

import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/pressable.dart';
import '../../../core/widgets/status_pill.dart';
import '../models/customer_address.dart';

/// One row of the address book (ui-templates `customer/addresses.html`): housing icon, label with the
/// default pill, the address on one line, and the total area as the number on the right.
/// Each address is its own pressable [NordicCard] (MOB-UI-14): a tap on the card chooses the address,
/// the pencil edits it, the bin deletes it.
class AddressCard extends StatelessWidget {
  final CustomerAddress address;
  final VoidCallback onSelect;
  final VoidCallback onEdit;
  final VoidCallback onDelete;

  /// The address the current booking uses: marked with a check.
  final bool selected;
  final bool grouped;

  const AddressCard({
    super.key,
    required this.address,
    required this.onSelect,
    required this.onEdit,
    required this.onDelete,
    this.selected = false,
    this.grouped = false,
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
    const padding = EdgeInsets.fromLTRB(
      NordicSpace.md,
      NordicSpace.sm,
      NordicSpace.xxs,
      NordicSpace.sm,
    );
    final content = Row(
      children: [
        Container(
          width: 40.0,
          height: 40.0,
          decoration: BoxDecoration(
            color: selected
                ? NordicColors.primaryFixed
                : NordicColors.surfaceLow,
            borderRadius: NordicRadius.tileAll,
          ),
          child: Icon(
            selected ? Icons.check : _getHousingIcon(),
            key: selected ? Key('address-selected-${address.addressId}') : null,
            color: NordicColors.primary,
          ),
        ),
        const SizedBox(width: NordicSpace.sm),
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
                    const SizedBox(width: NordicSpace.xxs),
                    Flexible(
                      child: FittedBox(
                        fit: BoxFit.scaleDown,
                        child: const StatusPill('Mặc định', tone: PillTone.ok),
                      ),
                    ),
                  ],
                ],
              ),
              const SizedBox(height: NordicSpace.xxs),
              Text(
                address.fullAddress,
                style: NordicTypography.bodySmall,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
            ],
          ),
        ),
        const SizedBox(width: NordicSpace.xs),
        Column(
          crossAxisAlignment: CrossAxisAlignment.end,
          children: [
            Text(
              _area,
              key: Key('address-area-${address.addressId}'),
              style: NordicTypography.titleSmall,
            ),
            const Text('m²', style: NordicTypography.bodySmall),
          ],
        ),
        IconButton(
          key: Key('address-edit-${address.addressId}'),
          icon: const Icon(Icons.edit_outlined, size: 20.0),
          color: NordicColors.textSecondary,
          tooltip: 'Sửa',
          onPressed: onEdit,
        ),
        IconButton(
          icon: const Icon(Icons.delete_outline, size: 20.0),
          color: NordicColors.textSecondary,
          tooltip: 'Xoá',
          onPressed: onDelete,
        ),
      ],
    );
    if (grouped) {
      return Pressable(
        key: Key('address-card-${address.addressId}'),
        child: InkWell(
          onTap: onSelect,
          child: Padding(padding: padding, child: content),
        ),
      );
    }
    return NordicCard(
      key: Key('address-card-${address.addressId}'),
      onTap: onSelect,
      padding: padding,
      child: content,
    );
  }
}
