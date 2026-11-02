import 'package:flutter/material.dart';

import '../../../core/device/camera_wrapper.dart';
import '../../../core/device/location_wrapper.dart';
import '../../../core/motion/error_shake.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/grouped_list_card.dart';
import '../../../core/widgets/mini_map_widget.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../../../core/widgets/stat_tile.dart';
import '../../../core/widgets/status_pill.dart';
import '../models/check_in_result.dart';
import '../services/dispatch_service.dart';
import '../../workers/routes.dart';

/// Screen for field arrival check-in with GPS tolerance <=100m and alternative fallback verification (MOB-M3-02).
/// MOB-UI-21 (`Mobile/DESIGN-SYSTEM-WORKER.md`): `NordicPage.pushed`, the main button in `actions`, tokens only,
/// a failed step shakes its message.
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

  /// Bumped on every failure so the message shakes.
  int _errors = 0;

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
        _errors++;
        _errorMessage = 'Chưa lấy được vị trí của bạn: $e';
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
        _errors++;
        _errorMessage = 'Chưa xác nhận được vị trí: $e';
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
        _errors++;
        _errorMessage = 'Chưa gửi được ảnh biển số: $e';
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
        _errors++;
        _errorMessage = 'Chưa nhờ được gia chủ xác nhận: $e';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return NordicPage.pushed(
      title: 'Đến nơi',
      actions: _buildActions(),
      children: _buildContent(),
    );
  }

  bool get _verified =>
      _checkInResult != null &&
      (_checkInResult!.isGpsVerified ||
          !_checkInResult!.requiresAlternativeVerification);

  /// The one main button of the state on screen, above the home indicator.
  List<Widget>? _buildActions() {
    if (_isLoading) return null;
    if (_verified) {
      return [
        NordicButton(
          key: const Key('continue_to_job_button'),
          label: 'Bắt đầu ca làm',
          onPressed: () => Navigator.of(context).pushReplacementNamed(
            WorkerRoutes.execution,
            arguments: widget.assignmentId,
          ),
        ),
      ];
    }
    if (_checkInResult != null) {
      return [
        NordicButton(
          label: 'Quét lại vị trí',
          variant: NordicButtonVariant.secondary,
          onPressed: _handleGpsCheckIn,
        ),
      ];
    }
    return [
      NordicButton(
        key: const Key('check_in_gps_button'),
        label: 'Tôi đã đến nơi',
        icon: const Icon(
          Icons.gps_fixed,
          color: NordicColors.textInverse,
          size: 20.0,
        ),
        onPressed: _handleGpsCheckIn,
      ),
    ];
  }

  List<Widget> _buildContent() {
    if (_isLoading) {
      return const [
        SizedBox(height: NordicSpace.xl),
        LoadingStateWidget(message: 'Đang xác nhận vị trí của bạn...'),
      ];
    }

    // Success view when verified (GPS or fallback)
    if (_verified) {
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

  Widget _errorBanner() => ErrorShake(
    errors: _errors,
    child: Container(
      padding: const EdgeInsets.all(NordicSpace.sm),
      decoration: const BoxDecoration(
        color: NordicColors.errorContainer,
        borderRadius: NordicRadius.tileAll,
      ),
      child: Text(
        _errorMessage!,
        style: NordicTypography.bodySmall.copyWith(color: NordicColors.error),
      ),
    ),
  );

  List<Widget> _buildInitialCheckInView() {
    return [
      const Text('Đã đến địa chỉ ca làm việc?', style: NordicTypography.h2),
      const SizedBox(height: NordicSpace.xxs),
      const Text(
        'Bấm nút bên dưới để kiểm tra vị trí của bạn. Cho phép lệch tối đa 100 mét.',
        style: NordicTypography.bodySmall,
      ),
      const SizedBox(height: NordicSpace.md),
      MiniMapWidget(
        targetLatitude: widget.destinationLatitude,
        targetLongitude: widget.destinationLongitude,
        workerLatitude: _currentGps?.latitude,
        workerLongitude: _currentGps?.longitude,
        addressLabel: 'Vị trí gia chủ',
        radiusMeters: 100.0,
      ),
      const SizedBox(height: NordicSpace.md),
      NordicCard(
        child: Row(
          children: [
            const Icon(
              Icons.location_on_outlined,
              color: NordicColors.primary,
              size: 22.0,
            ),
            const SizedBox(width: NordicSpace.xs),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const Text(
                    'Địa chỉ ca làm',
                    style: NordicTypography.titleSmall,
                  ),
                  const SizedBox(height: NordicSpace.xxs),
                  Text(
                    widget.destinationAddress,
                    style: NordicTypography.bodySmall,
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
      if (_errorMessage != null) ...[
        const SizedBox(height: NordicSpace.md),
        _errorBanner(),
      ],
    ];
  }

  List<Widget> _buildAlternativeVerificationView() {
    final distance = _checkInResult?.distanceMeters ?? 0.0;

    return [
      const Text('Vị trí chưa khớp', style: NordicTypography.h2),
      const SizedBox(height: NordicSpace.xs),
      Text(
        'Vị trí của bạn cách địa chỉ ca làm ${distance.toStringAsFixed(0)} mét, vượt mức cho phép 100 mét.',
        style: NordicTypography.bodySmall.copyWith(color: NordicColors.error),
      ),
      const SizedBox(height: NordicSpace.md),
      Row(
        children: [
          Expanded(
            child: StatTile(
              icon: Icons.straighten,
              label: 'Khoảng cách',
              value: distance.toStringAsFixed(0),
              unit: 'm',
            ),
          ),
          const SizedBox(width: NordicSpace.sm),
          const Expanded(
            child: NordicCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('Kết quả', style: NordicTypography.bodySmall),
                  SizedBox(height: NordicSpace.xs),
                  StatusPill('Ngoài vùng', tone: PillTone.bad, dot: true),
                ],
              ),
            ),
          ),
        ],
      ),
      const SizedBox(height: NordicSpace.lg),
      const Text('Chọn cách xác nhận khác', style: NordicTypography.titleSmall),
      const SizedBox(height: NordicSpace.xs),
      GroupedListCard(
        items: [
          GroupedListItem(
            key: const Key('take_plate_photo_button'),
            icon: Icons.camera_alt_outlined,
            title: _isSubmittingPhoto
                ? 'Đang tải ảnh...'
                : 'Chụp ảnh biển số nhà hoặc số phòng',
            onTap: _isSubmittingPhoto ? null : _handleCapturePlatePhoto,
          ),
          GroupedListItem(
            key: const Key('request_customer_confirmation_button'),
            icon: Icons.person_pin_circle_outlined,
            title: _isRequestingCustomerConfirmation
                ? 'Đang gửi yêu cầu...'
                : 'Nhờ gia chủ xác nhận trên ứng dụng',
            onTap: _isRequestingCustomerConfirmation
                ? null
                : _handleCustomerConfirmation,
          ),
        ],
      ),
      if (_capturedPlatePhoto != null) ...[
        const SizedBox(height: NordicSpace.xs),
        Text(
          'Đã chụp: ${_capturedPlatePhoto!.path}',
          style: NordicTypography.bodySmall,
        ),
      ],
      if (_errorMessage != null) ...[
        const SizedBox(height: NordicSpace.md),
        _errorBanner(),
      ],
    ];
  }

  List<Widget> _buildSuccessView() {
    final method = _checkInResult?.verificationMethod ?? 'GPS';
    String message = 'Đã xác nhận bằng vị trí của bạn.';
    if (method == 'PLATE_PHOTO') {
      message = 'Đã xác nhận bằng ảnh biển số nhà.';
    } else if (method == 'CUSTOMER_CONFIRMATION') {
      message = 'Gia chủ đã xác nhận bạn đến nơi.';
    }

    return [
      const SizedBox(height: NordicSpace.xl),
      const Icon(
        Icons.check_circle_rounded,
        size: 72.0,
        color: NordicColors.success,
      ),
      const SizedBox(height: NordicSpace.md),
      Text(
        'Bạn đã đến nơi',
        textAlign: TextAlign.center,
        style: NordicTypography.h2.copyWith(color: NordicColors.primary),
      ),
      const SizedBox(height: NordicSpace.xs),
      Text(
        message,
        textAlign: TextAlign.center,
        style: NordicTypography.bodyRegular,
      ),
      const SizedBox(height: NordicSpace.sm),
      const Center(
        child: StatusPill('Đã xác nhận', tone: PillTone.ok, dot: true),
      ),
    ];
  }
}
