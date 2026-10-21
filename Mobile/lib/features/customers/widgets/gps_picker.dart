import 'package:flutter/material.dart';

import '../../../core/device/device_location.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';

class LocationPreset {
  final String title;
  final double lat;
  final double lng;

  const LocationPreset({
    required this.title,
    required this.lat,
    required this.lng,
  });
}

/// GPS coordinates selector and quick preset picker.
///
/// "Lấy GPS Hiện Tại" reads the real position of the phone (foreground only, [IDeviceLocationProvider]); when it cannot, the
/// coordinates stay as they were, the reason is shown, and the presets and the manual entry still work.
class GpsCoordinatePicker extends StatefulWidget {
  final double latitude;
  final double longitude;
  final void Function(double lat, double lng) onCoordinatesChanged;

  /// Where the position comes from; the real device by default, a fake in tests.
  final IDeviceLocationProvider locationProvider;

  static const List<LocationPreset> presets = [
    LocationPreset(title: 'Q.1, TP.HCM', lat: 10.7769, lng: 106.7009),
    LocationPreset(title: 'Q.7, TP.HCM', lat: 10.7412, lng: 106.7028),
    LocationPreset(title: 'Bình Thạnh, TP.HCM', lat: 10.8031, lng: 106.7099),
    LocationPreset(title: 'Hoàn Kiếm, Hà Nội', lat: 21.0285, lng: 105.8542),
  ];

  const GpsCoordinatePicker({
    super.key,
    required this.latitude,
    required this.longitude,
    required this.onCoordinatesChanged,
    this.locationProvider = const GeolocatorLocationProvider(),
  });

  @override
  State<GpsCoordinatePicker> createState() => _GpsCoordinatePickerState();
}

class _GpsCoordinatePickerState extends State<GpsCoordinatePicker> {
  bool _locating = false;

  double get latitude => widget.latitude;
  double get longitude => widget.longitude;
  void Function(double lat, double lng) get onCoordinatesChanged =>
      widget.onCoordinatesChanged;

  /// Reads the real position once. Success fills the coordinates and says how accurate the fix is; every failure leaves them unchanged.
  Future<void> _useCurrentLocation() async {
    if (_locating) return;
    setState(() => _locating = true);
    final messenger = ScaffoldMessenger.of(context);
    String message;
    var ok = false;
    try {
      final point = await widget.locationProvider.currentPosition();
      if (!mounted) return;
      onCoordinatesChanged(point.latitude, point.longitude);
      ok = true;
      message =
          'Đã lấy vị trí hiện tại (${point.latitude.toStringAsFixed(5)}, ${point.longitude.toStringAsFixed(5)}), sai số khoảng ${point.accuracyMeters.round()} m';
    } on DeviceLocationException catch (e) {
      message = _failureMessage(e.failure);
    } catch (_) {
      message = _failureMessage(DeviceLocationFailure.failed);
    }
    if (!mounted) return;
    setState(() => _locating = false);
    messenger.showSnackBar(
      SnackBar(
        content: Text(message),
        duration: const Duration(seconds: 3),
        backgroundColor: ok ? NordicColors.primary : const Color(0xFF991B1B),
      ),
    );
  }

  static String _failureMessage(
    DeviceLocationFailure failure,
  ) => switch (failure) {
    DeviceLocationFailure.serviceDisabled => 'Định vị (GPS) của máy đang tắt. Hãy bật định vị, hoặc chọn vị trí mẫu / nhập toạ độ.',
    DeviceLocationFailure.permissionDenied => 'Ứng dụng chưa được phép dùng vị trí. Hãy cho phép khi được hỏi, hoặc chọn vị trí mẫu / nhập toạ độ.',
    DeviceLocationFailure.permissionDeniedForever => 'Quyền vị trí đã bị từ chối. Hãy bật lại trong Cài đặt của máy, hoặc chọn vị trí mẫu / nhập toạ độ.',
    DeviceLocationFailure.timeout => 'Chưa xác định được vị trí (hết thời gian chờ). Hãy ra nơi thoáng hơn rồi thử lại, hoặc chọn vị trí mẫu / nhập toạ độ.',
    DeviceLocationFailure.failed =>
      'Không lấy được vị trí hiện tại. Hãy chọn vị trí mẫu hoặc nhập toạ độ.',
  };

  void _showCoordinateInputDialog(BuildContext context) {
    final latController = TextEditingController(text: latitude.toString());
    final lngController = TextEditingController(text: longitude.toString());
    String? localError;

    showDialog<void>(
      context: context,
      builder: (dialogCtx) => StatefulBuilder(
        builder: (context, setDialogState) => AlertDialog(
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(16.0),
          ),
          backgroundColor: NordicColors.surface,
          title: Text('Nhập Toạ Độ GPS', style: NordicTypography.h3),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              TextField(
                controller: latController,
                keyboardType: const TextInputType.numberWithOptions(
                  decimal: true,
                  signed: true,
                ),
                decoration: const InputDecoration(
                  labelText: 'Vĩ độ (Latitude: -90 đến 90)',
                  border: OutlineInputBorder(),
                ),
              ),
              const SizedBox(height: 12.0),
              TextField(
                controller: lngController,
                keyboardType: const TextInputType.numberWithOptions(
                  decimal: true,
                  signed: true,
                ),
                decoration: const InputDecoration(
                  labelText: 'Kinh độ (Longitude: -180 đến 180)',
                  border: OutlineInputBorder(),
                ),
              ),
              if (localError != null) ...[
                const SizedBox(height: 8.0),
                Text(
                  localError!,
                  style: NordicTypography.bodySmall.copyWith(
                    color: NordicColors.error,
                  ),
                ),
              ],
            ],
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(dialogCtx).pop(),
              child: const Text('Huỷ'),
            ),
            ElevatedButton(
              onPressed: () {
                final lat = double.tryParse(latController.text);
                final lng = double.tryParse(lngController.text);
                if (lat == null || lat < -90 || lat > 90) {
                  setDialogState(
                    () => localError = 'Vĩ độ không hợp lệ (-90 đến 90)',
                  );
                  return;
                }
                if (lng == null || lng < -180 || lng > 180) {
                  setDialogState(
                    () => localError = 'Kinh độ không hợp lệ (-180 đến 180)',
                  );
                  return;
                }
                onCoordinatesChanged(lat, lng);
                Navigator.of(dialogCtx).pop();
              },
              style: ElevatedButton.styleFrom(
                backgroundColor: NordicColors.primary,
                foregroundColor: Colors.white,
              ),
              child: const Text('Lưu Toạ Độ'),
            ),
          ],
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Wrap(
          alignment: WrapAlignment.spaceBetween,
          crossAxisAlignment: WrapCrossAlignment.center,
          spacing: 8.0,
          children: [
            Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Icon(
                  Icons.location_on_outlined,
                  size: 18.0,
                  color: NordicColors.primary,
                ),
                const SizedBox(width: 6.0),
                Text('Toạ Độ GPS', style: NordicTypography.labelSmall),
              ],
            ),
            TextButton(
              onPressed: () => _showCoordinateInputDialog(context),
              style: TextButton.styleFrom(
                padding: EdgeInsets.zero,
                minimumSize: const Size(50, 30),
                tapTargetSize: MaterialTapTargetSize.shrinkWrap,
              ),
              child: Text(
                'Nhập số liệu',
                style: NordicTypography.bodySmall.copyWith(
                  color: NordicColors.primary,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ),
          ],
        ),
        const SizedBox(height: 8.0),
        Container(
          padding: const EdgeInsets.symmetric(horizontal: 12.0, vertical: 10.0),
          decoration: BoxDecoration(
            color: NordicColors.surfaceSubtle,
            borderRadius: BorderRadius.circular(10.0),
            border: Border.all(color: NordicColors.border),
          ),
          child: Row(
            children: [
              Expanded(
                child: Text(
                  'Vĩ độ: ${latitude.toStringAsFixed(4)}  •  Kinh độ: ${longitude.toStringAsFixed(4)}',
                  style: NordicTypography.bodySmall.copyWith(
                    fontWeight: FontWeight.w600,
                    color: NordicColors.textTitle,
                  ),
                ),
              ),
              const Icon(
                Icons.my_location_rounded,
                size: 16.0,
                color: NordicColors.secondaryAccent,
              ),
            ],
          ),
        ),
        const SizedBox(height: 10.0),
        Text('Vị trí mẫu nhanh:', style: NordicTypography.labelSmall),
        const SizedBox(height: 6.0),
        Wrap(
          spacing: 6.0,
          runSpacing: 6.0,
          children: GpsCoordinatePicker.presets.map((preset) {
            final isSelected =
                (latitude - preset.lat).abs() < 0.001 &&
                (longitude - preset.lng).abs() < 0.001;
            return ChoiceChip(
              label: Text(preset.title),
              labelStyle: TextStyle(
                fontSize: 12.0,
                fontWeight: isSelected ? FontWeight.w700 : FontWeight.w500,
                color: isSelected ? Colors.white : NordicColors.textBody,
              ),
              selected: isSelected,
              selectedColor: NordicColors.primary,
              backgroundColor: NordicColors.surface,
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(8.0),
                side: BorderSide(
                  color: isSelected
                      ? NordicColors.primary
                      : NordicColors.border,
                ),
              ),
              onSelected: (_) => onCoordinatesChanged(preset.lat, preset.lng),
            );
          }).toList(),
        ),
        const SizedBox(height: 8.0),
        NordicButton(
          label: 'Lấy GPS Hiện Tại',
          variant: NordicButtonVariant.secondary,
          icon: const Icon(
            Icons.gps_fixed,
            size: 16.0,
            color: NordicColors.primary,
          ),
          width: double.infinity,
          isLoading: _locating,
          onPressed: _useCurrentLocation,
        ),
      ],
    );
  }
}
