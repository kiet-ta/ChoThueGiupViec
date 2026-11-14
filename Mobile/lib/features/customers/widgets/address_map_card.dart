import 'package:flutter/material.dart';

import '../../../core/device/device_location.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_card.dart';
import '../logic/map_address.dart';
import '../screens/address_map_screen.dart';

/// The entry to the address map in a form (ADDR-MAP-01). It is a card, not a live map: a map inside a scrolling form fights
/// the scroll for every drag, so the map opens full screen on a tap.
class AddressMapCard extends StatelessWidget {
  /// Whether the address already has a point chosen on the map (a saved address, or a point picked in this form).
  final bool hasPoint;
  final VoidCallback onTap;

  const AddressMapCard({
    super.key,
    required this.hasPoint,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    return Semantics(
      button: true,
      child: NordicCard(
        key: const Key('address-map-card'),
        onTap: onTap,
        child: Row(
          children: [
            Container(
              width: 44.0,
              height: 44.0,
              decoration: const BoxDecoration(
                color: NordicColors.primaryFixed,
                shape: BoxShape.circle,
              ),
              child: const Icon(
                Icons.map_outlined,
                color: NordicColors.primary,
              ),
            ),
            const SizedBox(width: NordicSpace.sm),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    hasPoint
                        ? 'Đã chọn vị trí trên bản đồ'
                        : 'Chọn nhà trên bản đồ',
                    style: NordicTypography.titleSmall,
                  ),
                  const SizedBox(height: NordicSpace.xxs),
                  Text(
                    hasPoint
                        ? 'Chạm để xem hoặc đổi vị trí.'
                        : 'Chạm vào nhà bạn trên bản đồ, địa chỉ sẽ tự điền.',
                    style: NordicTypography.bodySmall.copyWith(
                      color: NordicColors.textSecondary,
                    ),
                  ),
                ],
              ),
            ),
            const Icon(
              Icons.chevron_right_rounded,
              color: NordicColors.textSecondary,
            ),
          ],
        ),
      ),
    );
  }
}

/// Opens "Chọn vị trí nhà" at the saved point ([hasPoint]) or without one; null when the customer goes back without choosing.
Future<PickedPlace?> openAddressMap(
  BuildContext context, {
  required bool hasPoint,
  required double latitude,
  required double longitude,
  required ReverseGeocode reverseGeocode,
  IDeviceLocationProvider? locationProvider,
  bool showTiles = true,
}) {
  return Navigator.of(context).push<PickedPlace>(
    MaterialPageRoute(
      builder: (_) => AddressMapScreen(
        initialLatitude: hasPoint ? latitude : null,
        initialLongitude: hasPoint ? longitude : null,
        reverseGeocode: reverseGeocode,
        locationProvider:
            locationProvider ?? const GeolocatorLocationProvider(),
        showTiles: showTiles,
      ),
    ),
  );
}
