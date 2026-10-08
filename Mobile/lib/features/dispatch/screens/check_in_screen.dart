import 'package:flutter/material.dart';
import '../../../core/device/camera_wrapper.dart';
import '../../../core/device/location_wrapper.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/eyebrow_badge.dart';
import '../../../core/widgets/mini_map_widget.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../models/check_in_result.dart';
import '../services/dispatch_service.dart';

/// Screen for field arrival check-in with GPS tolerance <=100m and alternative fallback verification (MOB-M3-02).
class CheckInScreen extends StatefulWidget {
  final int assignmentId;
  final double destinationLatitude;
  final double destinationLongitude;
  final String destinationAddress;
  final DispatchService? dispatchService;

  const CheckInScreen({
    super.key,
    required this.assignmentId,
    this.destinationLatitude = 21.028511,
    this.destinationLongitude = 105.854167,
    this.destinationAddress = '123 Đường Cầu Giấy, Quan Hoa, Cầu Giấy, Hà Nội',
    this.dispatchService,
  });

  @override
  State<CheckInScreen> createState() => _CheckInScreenState();
}

class _CheckInScreenState extends State<CheckInScreen> {
  late final DispatchService _dispatchService;
  bool _isLoading = false;
  String? _errorMessage;
  CheckInResult? _checkInResult;
  GeoPoint? _currentGps;
  CapturedImage? _capturedPlatePhoto;
  bool _isSubmittingPhoto = false;
  bool _isRequestingCustomerConfirmation = false;

  @override
  void initState() {
    super.initState();
    _dispatchService = widget.dispatchService ?? DispatchService();
    _refreshCurrentGps();
  }

  Future<void> _refreshCurrentGps() async {
    try {
      final loc = await _dispatchService.locationWrapper.getCurrentLocation();
      if (!mounted) return;
      setState(() {
        _currentGps = loc;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _errorMessage = 'Không lấy được toạ độ GPS: $e';
      });
    }
  }

  Future<void> _handleGpsCheckIn() async {
    setState(() {
      _isLoading = true;
      _errorMessage = null;
    });

    try {
      final loc = await _dispatchService.locationWrapper.getCurrentLocation();
      _currentGps = loc;

      final result = await _dispatchService.checkInGps(
        assignmentId: widget.assignmentId,
        latitude: loc.latitude,
        longitude: loc.longitude,
        destinationLatitude: widget.destinationLatitude,
        destinationLongitude: widget.destinationLongitude,
      );

      if (!mounted) return;
      setState(() {
        _checkInResult = result;
        _isLoading = false;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _isLoading = false;
        _errorMessage = 'Check-in thất bại: $e';
      });
    }
  }

  Future<void> _handleCapturePlatePhoto() async {
    setState(() {
      _errorMessage = null;
    });

    try {
      final photo = await _dispatchService.cameraWrapper.capturePhoto();
      if (photo == null || !mounted) return;

      setState(() {
        _capturedPlatePhoto = photo;
        _isSubmittingPhoto = true;
      });

      final result = await _dispatchService.submitPlatePhoto(
        assignmentId: widget.assignmentId,
        platePhotoUrl: photo.path,
      );

      if (!mounted) return;
      setState(() {
        _checkInResult = result;
        _isSubmittingPhoto = false;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _isSubmittingPhoto = false;
        _errorMessage = 'Gửi ảnh biển số thất bại: $e';
      });
    }
  }

  Future<void> _handleCustomerConfirmation() async {
    setState(() {
      _isRequestingCustomerConfirmation = true;
      _errorMessage = null;
    });

    try {
      final result = await _dispatchService.requestCustomerConfirmation(
        assignmentId: widget.assignmentId,
      );

      if (!mounted) return;
      setState(() {
        _checkInResult = result;
        _isRequestingCustomerConfirmation = false;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _isRequestingCustomerConfirmation = false;
        _errorMessage = 'Xác nhận từ khách hàng thất bại: $e';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(
        title: const Text('Check-in Hiện Trường'),
      ),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: _buildContent(),
        ),
      ),
    );
  }

  Widget _buildContent() {
    if (_isLoading) {
      return const LoadingStateWidget(
        message: 'Đang xác thực vị trí GPS tại hiện trường...',
      );
    }

    // Success view when verified (GPS or fallback)
    if (_checkInResult != null &&
        (_checkInResult!.isGpsVerified ||
            !_checkInResult!.requiresAlternativeVerification)) {
      return _buildSuccessView();
    }

    // Fallback required view when GPS distance > 100m
    if (_checkInResult != null &&
        _checkInResult!.requiresAlternativeVerification) {
      return _buildAlternativeVerificationView();
    }

    // Initial check-in screen view
    return _buildInitialCheckInView();
  }

  Widget _buildInitialCheckInView() {
    return ListView(
      padding: const EdgeInsets.all(16.0),
      children: [
        const EyebrowBadge(text: 'XÁC THỰC HIỆN TRƯỜNG (BR-04)'),
        const SizedBox(height: 8.0),
        const Text(
          'Đã đến địa chỉ ca làm việc?',
          style: NordicTypography.h2,
        ),
        const SizedBox(height: 4.0),
        Text(
          'Vui lòng bấm nút dưới đây để kiểm tra toạ độ hiện tại. Dung sai cho phép ≤ 100 mét.',
          style: NordicTypography.bodyRegular,
        ),
        const SizedBox(height: 16.0),

        // Destination Card
        NordicCard(
          child: Padding(
            padding: const EdgeInsets.all(16.0),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    const Icon(Icons.home_outlined, color: NordicColors.primary, size: 22.0),
                    const SizedBox(width: 8.0),
                    Expanded(
                      child: Text(
                        'Địa chỉ đơn hàng #${widget.assignmentId}',
                        style: NordicTypography.h3,
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 8.0),
                Text(
                  widget.destinationAddress,
                  style: NordicTypography.bodyRegular,
                ),
                const SizedBox(height: 16.0),
                MiniMapWidget(
                  targetLatitude: widget.destinationLatitude,
                  targetLongitude: widget.destinationLongitude,
                  workerLatitude: _currentGps?.latitude,
                  workerLongitude: _currentGps?.longitude,
                  addressLabel: 'Vị trí khách',
                  radiusMeters: 100.0,
                ),
              ],
            ),
          ),
        ),
        const SizedBox(height: 16.0),

        if (_errorMessage != null) ...[
          Container(
            padding: const EdgeInsets.all(12.0),
            margin: const EdgeInsets.only(bottom: 16.0),
            decoration: BoxDecoration(
              color: NordicColors.errorContainer,
              borderRadius: BorderRadius.circular(8.0),
              border: Border.all(color: NordicColors.error),
            ),
            child: Text(
              _errorMessage!,
              style: NordicTypography.bodySmall.copyWith(color: NordicColors.error),
            ),
          ),
        ],

        NordicButton(
          key: const Key('check_in_gps_button'),
          label: 'Đã Đến Nơi (GPS)',
          icon: const Icon(Icons.gps_fixed, color: Colors.white, size: 20.0),
          onPressed: _handleGpsCheckIn,
        ),
      ],
    );
  }

  Widget _buildAlternativeVerificationView() {
    final distance = _checkInResult?.distanceMeters ?? 0.0;

    return ListView(
      padding: const EdgeInsets.all(16.0),
      children: [
        const EyebrowBadge(text: 'LỆCH GPS > 100M'),
        const SizedBox(height: 8.0),
        const Text(
          'Vị trí chưa khớp',
          style: NordicTypography.h2,
        ),
        const SizedBox(height: 8.0),
        Text(
          'Toạ độ GPS của bạn cách địa chỉ đơn ${distance.toStringAsFixed(0)} mét (vượt mức cho phép ≤ 100m).',
          style: NordicTypography.bodyRegular.copyWith(color: NordicColors.error),
        ),
        const SizedBox(height: 16.0),

        NordicCard(
          isHighlighted: true,
          child: Padding(
            padding: const EdgeInsets.all(16.0),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const Text(
                  'Chọn phương thức xác nhận thay thế:',
                  style: NordicTypography.h3,
                ),
                const SizedBox(height: 16.0),

                // Option 1: Plate photo
                OutlinedButton.icon(
                  key: const Key('take_plate_photo_button'),
                  onPressed: _isSubmittingPhoto ? null : _handleCapturePlatePhoto,
                  icon: const Icon(Icons.camera_alt_outlined),
                  label: Text(_isSubmittingPhoto
                      ? 'Đang tải ảnh...'
                      : 'Chụp ảnh biển số nhà / số phòng'),
                  style: OutlinedButton.styleFrom(
                    padding: const EdgeInsets.symmetric(vertical: 14.0),
                  ),
                ),
                if (_capturedPlatePhoto != null) ...[
                  const SizedBox(height: 8.0),
                  Text(
                    'Đã chụp: ${_capturedPlatePhoto!.path}',
                    style: NordicTypography.bodySmall,
                  ),
                ],
                const SizedBox(height: 12.0),

                // Option 2: Customer confirmation
                OutlinedButton.icon(
                  key: const Key('request_customer_confirmation_button'),
                  onPressed: _isRequestingCustomerConfirmation
                      ? null
                      : _handleCustomerConfirmation,
                  icon: const Icon(Icons.person_pin_circle_outlined),
                  label: Text(_isRequestingCustomerConfirmation
                      ? 'Đang gửi yêu cầu...'
                      : 'Nhờ khách xác nhận trên ứng dụng'),
                  style: OutlinedButton.styleFrom(
                    padding: const EdgeInsets.symmetric(vertical: 14.0),
                  ),
                ),
              ],
            ),
          ),
        ),
        const SizedBox(height: 16.0),

        NordicButton(
          label: 'Thử quét lại GPS',
          variant: NordicButtonVariant.secondary,
          onPressed: _handleGpsCheckIn,
        ),
      ],
    );
  }

  Widget _buildSuccessView() {
    final method = _checkInResult?.verificationMethod ?? 'GPS';
    String message = 'Check-in thành công bằng định vị GPS!';
    if (method == 'PLATE_PHOTO') {
      message = 'Check-in thành công bằng ảnh chụp biển số nhà!';
    } else if (method == 'CUSTOMER_CONFIRMATION') {
      message = 'Check-in thành công do khách hàng đã xác nhận!';
    }

    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24.0),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(
              Icons.check_circle_rounded,
              size: 72.0,
              color: NordicColors.success,
            ),
            const SizedBox(height: 16.0),
            Text(
              'Đã Đến Nơi Thành Công',
              textAlign: TextAlign.center,
              style: NordicTypography.h2.copyWith(color: NordicColors.primary),
            ),
            const SizedBox(height: 8.0),
            Text(
              message,
              textAlign: TextAlign.center,
              style: NordicTypography.bodyRegular,
            ),
            const SizedBox(height: 24.0),
            NordicButton(
              key: const Key('continue_to_job_button'),
              label: 'Bắt đầu ca làm',
              onPressed: () {
                Navigator.of(context).maybePop();
              },
            ),
          ],
        ),
      ),
    );
  }
}
