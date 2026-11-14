import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';

import '../../../core/device/device_location.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_page.dart';
import '../logic/map_address.dart';
import '../models/reverse_geocode_result.dart';

/// Reads the address of a point (the server's `POST /api/v1/locations/reverse-geocode`).
typedef ReverseGeocode = Future<ReverseGeocodeResult> Function(
  double latitude,
  double longitude,
);

/// "Chọn vị trí nhà" (ADDR-MAP-01): the customer moves the map, taps the house and sees the address the server reads for that
/// point; "Dùng vị trí này" returns a [PickedPlace]. The map opens at the saved point, else at the phone's position (moving
/// the map only: the customer may not be at home), else at District 1.
class AddressMapScreen extends StatefulWidget {
  /// The address's saved point, or null for a new address.
  final double? initialLatitude;
  final double? initialLongitude;
  final ReverseGeocode reverseGeocode;
  final IDeviceLocationProvider locationProvider;

  /// False in widget tests: no tile is fetched from the network.
  final bool showTiles;

  const AddressMapScreen({
    super.key,
    this.initialLatitude,
    this.initialLongitude,
    required this.reverseGeocode,
    this.locationProvider = const GeolocatorLocationProvider(),
    this.showTiles = true,
  });

  @override
  State<AddressMapScreen> createState() => _AddressMapScreenState();
}

class _AddressMapScreenState extends State<AddressMapScreen> {
  final _map = MapController();
  final _mapReady = Completer<void>();
  final _latest = LatestRequest();

  LatLng? _pin;
  ReverseGeocodeResult? _address;
  bool _reading = false;
  bool _readFailed = false;
  bool _locating = false;
  String? _locateError;

  bool get _hasSavedPoint =>
      widget.initialLatitude != null && widget.initialLongitude != null;

  @override
  void initState() {
    super.initState();
    if (_hasSavedPoint) {
      _pin = LatLng(widget.initialLatitude!, widget.initialLongitude!);
    } else {
      unawaited(_showPhonePosition());
    }
  }

  @override
  void dispose() {
    _map.dispose();
    super.dispose();
  }

  /// Without a saved point the map starts where the phone is, without a pin; a failure leaves it on District 1, silently.
  Future<void> _showPhonePosition() async {
    try {
      final point = await widget.locationProvider.currentPosition();
      await _mapReady.future;
      if (!mounted || _pin != null) return;
      _map.move(LatLng(point.latitude, point.longitude), houseZoom);
    } catch (_) {
      // The customer can still move the map or tap "Vị trí của tôi".
    }
  }

  /// Puts the pin at the point and reads its address; only the answer for the latest point is kept.
  Future<void> _place(double latitude, double longitude) async {
    final point = LatLng(
      double.parse(latitude.toStringAsFixed(6)),
      double.parse(longitude.toStringAsFixed(6)),
    );
    final ticket = _latest.next();
    setState(() {
      _pin = point;
      _address = null;
      _reading = true;
      _readFailed = false;
      _locateError = null;
    });
    try {
      final result = await widget.reverseGeocode(
        point.latitude,
        point.longitude,
      );
      if (!mounted || !_latest.isLatest(ticket)) return;
      setState(() {
        _address = result;
        _reading = false;
      });
    } catch (_) {
      if (!mounted || !_latest.isLatest(ticket)) return;
      setState(() {
        _reading = false;
        _readFailed = true;
      });
    }
  }

  /// "Vị trí của tôi": moves the map to the phone and puts the pin there.
  Future<void> _locate() async {
    if (_locating) return;
    setState(() {
      _locating = true;
      _locateError = null;
    });
    try {
      final point = await widget.locationProvider.currentPosition();
      if (!mounted) return;
      setState(() => _locating = false);
      _map.move(LatLng(point.latitude, point.longitude), houseZoom);
      await _place(point.latitude, point.longitude);
    } on DeviceLocationException catch (e) {
      if (!mounted) return;
      setState(() {
        _locating = false;
        _locateError = deviceLocationFailureText(e.failure);
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _locating = false;
        _locateError = deviceLocationFailureText(DeviceLocationFailure.failed);
      });
    }
  }

  void _confirm() {
    final pin = _pin;
    if (pin == null) return;
    Navigator.of(context).pop(
      PickedPlace(
        latitude: pin.latitude,
        longitude: pin.longitude,
        address: _address,
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final start = _pin ?? const LatLng(defaultMapLatitude, defaultMapLongitude);
    return NordicPage.pushed(
      title: 'Chọn vị trí nhà',
      bodyOverride: Stack(
        children: [
          Positioned.fill(
            child: FlutterMap(
              key: const Key('address-map'),
              mapController: _map,
              options: MapOptions(
                initialCenter: start,
                initialZoom: _pin != null ? houseZoom : districtZoom,
                interactionOptions: const InteractionOptions(
                  flags: InteractiveFlag.all & ~InteractiveFlag.rotate,
                ),
                onMapReady: () {
                  if (!_mapReady.isCompleted) _mapReady.complete();
                },
                onTap: (_, point) => _place(point.latitude, point.longitude),
              ),
              children: [
                if (widget.showTiles)
                  TileLayer(
                    urlTemplate: AddressMapTiles.urlTemplate,
                    userAgentPackageName: AddressMapTiles.userAgentPackageName,
                  ),
                if (_pin != null)
                  MarkerLayer(
                    markers: [
                      Marker(
                        point: _pin!,
                        width: 48.0,
                        height: 48.0,
                        alignment: Alignment.topCenter,
                        child: Semantics(
                          label: 'Vị trí nhà bạn',
                          child: const Icon(
                            Icons.location_on,
                            key: Key('address-map-pin'),
                            size: 48.0,
                            color: NordicColors.primary,
                          ),
                        ),
                      ),
                    ],
                  ),
                if (widget.showTiles)
                  const SimpleAttributionWidget(
                    alignment: Alignment.topRight,
                    source: Text(AddressMapTiles.attribution),
                  ),
              ],
            ),
          ),
          Positioned(
            left: NordicSpace.gutter,
            right: NordicSpace.gutter,
            bottom: NordicSpace.md,
            child: NordicCard(child: _panel()),
          ),
        ],
      ),
      actions: [
        NordicButton(
          key: const Key('address-map-confirm'),
          label: 'Dùng vị trí này',
          width: double.infinity,
          onPressed: _pin == null || _reading ? null : _confirm,
        ),
      ],
      children: const [],
    );
  }

  /// What the customer has chosen so far, and "Vị trí của tôi".
  Widget _panel() {
    final (String title, String? body) = _pin == null
        ? (
            'Chạm vào nhà bạn trên bản đồ',
            'Kéo và phóng to bản đồ để tìm nhà, rồi chạm đúng chỗ đó.',
          )
        : _reading
        ? ('Đang tìm địa chỉ…', null)
        : _readFailed
        ? (
            'Đã chọn vị trí',
            'Không đọc được địa chỉ ở điểm này. Bạn có thể nhập tay.',
          )
        : _address != null
        ? ('Địa chỉ ở điểm này', placeText(_address!))
        : ('Vị trí đã lưu', 'Chạm chỗ khác nếu nhà bạn không ở đây.');
    return Column(
      key: const Key('address-map-status'),
      crossAxisAlignment: CrossAxisAlignment.stretch,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(title, style: NordicTypography.titleSmall),
        if (body != null) ...[
          const SizedBox(height: NordicSpace.xxs),
          Text(
            body,
            style: NordicTypography.bodyRegular.copyWith(
              color: NordicColors.textSecondary,
            ),
          ),
        ],
        if (_locateError != null) ...[
          const SizedBox(height: NordicSpace.xs),
          Text(
            _locateError!,
            style: NordicTypography.bodySmall.copyWith(
              color: NordicColors.error,
            ),
          ),
        ],
        const SizedBox(height: NordicSpace.sm),
        NordicButton(
          key: const Key('address-map-locate'),
          label: 'Vị trí của tôi',
          variant: NordicButtonVariant.secondary,
          icon: const Icon(
            Icons.my_location_rounded,
            size: 18.0,
            color: NordicColors.primary,
          ),
          width: double.infinity,
          isLoading: _locating,
          onPressed: _locate,
        ),
      ],
    );
  }
}
