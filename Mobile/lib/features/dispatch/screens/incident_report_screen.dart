import 'package:flutter/material.dart';

import '../../../core/device/camera_wrapper.dart';
import '../../../core/device/location_wrapper.dart';
import '../../../core/motion/error_shake.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/pressable.dart';
import '../../../core/widgets/status_pill.dart';
import '../../../core/widgets/step_dots.dart';
import '../models/incident_log.dart';
import '../services/dispatch_service.dart';

/// Screen for reporting force majeure incidents with camera evidence and GPS (MOB-M3-04, BR-10).
/// MOB-UI-22 (`Mobile/DESIGN-SYSTEM-WORKER.md`): `NordicPage.pushed`, the send button in `actions`,
/// tokens only, a failed step shakes its message.
class IncidentReportScreen extends StatefulWidget {
  final int assignmentId;
  final DispatchService? dispatchService;

  const IncidentReportScreen({
    super.key,
    required this.assignmentId,
    this.dispatchService,
  });

  @override
  State<IncidentReportScreen> createState() => _IncidentReportScreenState();
}

class _IncidentReportScreenState extends State<IncidentReportScreen> {
  late final DispatchService _dispatchService;
  String _selectedType = 'ACCIDENT';
  final TextEditingController _descriptionController = TextEditingController();
  CapturedImage? _capturedEvidence;
  GeoPoint? _currentGps;
  bool _isLoadingGps = false;
  bool _isSubmitting = false;
  String? _errorMessage;
  IncidentLog? _submittedIncident;

  /// Bumped on every failure so the message shakes.
  int _errors = 0;

  final Map<String, String> _incidentTypes = {
    'ACCIDENT': 'Tai nạn giao thông / Hỏng xe',
    'HEALTH': 'Sức khỏe đột xuất / Ngộ độc',
    'SEVERE_WEATHER': 'Thiên tai / Mưa bão ngập lụt',
    'OTHER': 'Sự cố bất khả kháng khác',
  };

  @override
  void initState() {
    super.initState();
    _dispatchService = widget.dispatchService ?? DispatchService();
    _fetchLocation();
  }

  @override
  void dispose() {
    _descriptionController.dispose();
    super.dispose();
  }

  Future<void> _fetchLocation() async {
    setState(() {
      _isLoadingGps = true;
    });

    try {
      final loc = await _dispatchService.locationWrapper.getCurrentLocation();
      if (!mounted) return;
      setState(() {
        _currentGps = loc;
        _isLoadingGps = false;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _isLoadingGps = false;
      });
    }
  }

  void _fail(String message) {
    setState(() {
      _isSubmitting = false;
      _errors++;
      _errorMessage = message;
    });
  }

  Future<void> _handleCapturePhoto() async {
    try {
      final photo = await _dispatchService.cameraWrapper.capturePhoto();
      if (photo != null && mounted) {
        setState(() {
          _capturedEvidence = photo;
          _errorMessage = null;
        });
      }
    } catch (e) {
      if (mounted) _fail('Chưa chụp được ảnh: $e');
    }
  }

  Future<void> _handleSubmitIncident() async {
    if (_capturedEvidence == null) {
      _fail(
        'Bạn chưa chụp ảnh hiện trường. Hãy chụp ảnh bằng chứng trước khi gửi.',
      );
      return;
    }

    if (_currentGps == null) {
      await _fetchLocation();
      if (_currentGps == null) {
        if (mounted) _fail('Chưa lấy được vị trí của sự cố. Hãy thử lại.');
        return;
      }
    }

    setState(() {
      _isSubmitting = true;
      _errorMessage = null;
    });

    try {
      final log = await _dispatchService.reportIncident(
        assignmentId: widget.assignmentId,
        incidentType: _selectedType,
        description: _descriptionController.text.trim().isEmpty
            ? _incidentTypes[_selectedType]!
            : _descriptionController.text.trim(),
        photoEvidenceUrl: _capturedEvidence!.path,
        latitude: _currentGps!.latitude,
        longitude: _currentGps!.longitude,
      );

      if (!mounted) return;
      setState(() {
        _submittedIncident = log;
        _isSubmitting = false;
      });
    } catch (e) {
      if (!mounted) return;
      _fail('Chưa gửi được báo cáo sự cố: $e');
    }
  }

  @override
  Widget build(BuildContext context) {
    final submitted = _submittedIncident;
    return NordicPage.pushed(
      title: 'Báo sự cố',
      actions: [
        if (submitted == null)
          NordicButton(
            key: const Key('submit_incident_button'),
            label: 'Gửi báo cáo',
            isLoading: _isSubmitting,
            onPressed: _isSubmitting ? null : _handleSubmitIncident,
          )
        else
          NordicButton(
            key: const Key('finish_incident_button'),
            label: 'Đã hiểu',
            onPressed: () {
              Navigator.of(context).maybePop();
            },
          ),
      ],
      children: submitted != null
          ? _buildSubmittedView(submitted)
          : _buildFormView(),
    );
  }

  static const _incidentIcons = {
    'ACCIDENT': Icons.car_crash_outlined,
    'HEALTH': Icons.medical_services_outlined,
    'SEVERE_WEATHER': Icons.thunderstorm_outlined,
    'OTHER': Icons.more_horiz,
  };

  /// One tile of the 2 x 2 grid of incident types, the chosen one with a primary border.
  Widget _kindTile(String code) {
    final on = _selectedType == code;
    return Pressable(
      child: InkWell(
        key: Key('incident-kind-$code'),
        borderRadius: NordicRadius.cardAll,
        onTap: () => setState(() => _selectedType = code),
        child: Container(
          padding: const EdgeInsets.all(NordicSpace.md),
          decoration: BoxDecoration(
            color: on ? NordicColors.primaryFixed : NordicColors.surface,
            borderRadius: NordicRadius.cardAll,
            border: Border.all(
              color: on ? NordicColors.primary : NordicColors.border,
              width: on ? 2.0 : 1.0,
            ),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Icon(_incidentIcons[code], color: NordicColors.primary),
              const SizedBox(height: NordicSpace.sm),
              Text(
                _incidentTypes[code]!,
                style: NordicTypography.titleSmall,
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
              ),
            ],
          ),
        ),
      ),
    );
  }

  List<Widget> _buildFormView() {
    final codes = _incidentTypes.keys.toList();
    return [
      const Text(
        'Bạn gặp sự cố trên đường hay tại nơi làm?',
        style: NordicTypography.h2,
      ),
      const SizedBox(height: NordicSpace.xxs),
      const Text(
        'Gửi báo cáo kèm ảnh và vị trí để được miễn phạt. Ứng dụng sẽ tìm người làm thay trong 5 phút.',
        style: NordicTypography.bodySmall,
      ),
      const SizedBox(height: NordicSpace.md),

      // Incident type: 2 x 2 tiles
      Row(
        children: [
          Expanded(child: _kindTile(codes[0])),
          const SizedBox(width: NordicSpace.sm),
          Expanded(child: _kindTile(codes[1])),
        ],
      ),
      const SizedBox(height: NordicSpace.sm),
      Row(
        children: [
          Expanded(child: _kindTile(codes[2])),
          const SizedBox(width: NordicSpace.sm),
          Expanded(child: _kindTile(codes[3])),
        ],
      ),

      const SizedBox(height: NordicSpace.lg),
      const Text('Mô tả', style: NordicTypography.titleSmall),
      const SizedBox(height: NordicSpace.xs),
      TextField(
        key: const Key('incident_description_input'),
        controller: _descriptionController,
        maxLines: 3,
        decoration: const InputDecoration(
          hintText: 'Ví dụ: Xe bị thủng lốp tại ngã tư Cầu Giấy, đang tìm tiệm vá xe...',
        ),
      ),

      const SizedBox(height: NordicSpace.lg),
      const Text('Ảnh và vị trí', style: NordicTypography.titleSmall),
      const SizedBox(height: NordicSpace.xs),
      // Camera evidence & GPS
      NordicCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            NordicButton(
              key: const Key('take_incident_photo_button'),
              variant: NordicButtonVariant.secondary,
              icon: const Icon(Icons.add_a_photo_outlined, size: 18.0),
              label: _capturedEvidence != null
                  ? 'Chụp lại ảnh'
                  : 'Chụp ảnh bằng chứng',
              onPressed: _handleCapturePhoto,
            ),
            if (_capturedEvidence != null) ...[
              const SizedBox(height: NordicSpace.xs),
              Row(
                children: [
                  const Icon(
                    Icons.check_circle,
                    color: NordicColors.primary,
                    size: 16.0,
                  ),
                  const SizedBox(width: NordicSpace.xs),
                  Expanded(
                    child: Text(
                      'Đã chụp: ${_capturedEvidence!.path}',
                      style: NordicTypography.bodySmall,
                      overflow: TextOverflow.ellipsis,
                    ),
                  ),
                ],
              ),
            ],
            const SizedBox(height: NordicSpace.sm),
            Row(
              children: [
                Icon(
                  _currentGps != null
                      ? Icons.location_on
                      : Icons.location_searching,
                  color: _currentGps != null
                      ? NordicColors.primary
                      : NordicColors.accentAmber,
                  size: 18.0,
                ),
                const SizedBox(width: NordicSpace.xs),
                Expanded(
                  child: Text(
                    _isLoadingGps
                        ? 'Đang lấy vị trí...'
                        : _currentGps != null
                        ? 'Vị trí: ${_currentGps!.latitude.toStringAsFixed(4)}, ${_currentGps!.longitude.toStringAsFixed(4)}'
                        : 'Chưa lấy được vị trí',
                    style: NordicTypography.bodySmall,
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
      const SizedBox(height: NordicSpace.md),

      // What happens next
      const NordicCard(
        child: StepDots(
          steps: [
            StepItem('Báo', DotState.current),
            StepItem('Tìm người thay', DotState.todo),
            StepItem('Người mới', DotState.todo),
          ],
        ),
      ),

      ErrorShake(
        errors: _errors,
        child: _errorMessage == null
            ? const SizedBox.shrink()
            : Padding(
                padding: const EdgeInsets.only(top: NordicSpace.md),
                child: Container(
                  padding: const EdgeInsets.all(NordicSpace.sm),
                  decoration: const BoxDecoration(
                    color: NordicColors.errorContainer,
                    borderRadius: NordicRadius.tileAll,
                  ),
                  child: Text(
                    _errorMessage!,
                    style: NordicTypography.bodySmall.copyWith(
                      color: NordicColors.error,
                    ),
                  ),
                ),
              ),
      ),
    ];
  }

  Widget _row(String label, Widget value) {
    return Row(
      mainAxisAlignment: MainAxisAlignment.spaceBetween,
      children: [
        Expanded(child: Text(label, style: NordicTypography.bodySmall)),
        const SizedBox(width: NordicSpace.xs),
        value,
      ],
    );
  }

  List<Widget> _buildSubmittedView(IncidentLog incident) {
    final deadline =
        '${incident.reDispatchDeadline.hour.toString().padLeft(2, '0')}:${incident.reDispatchDeadline.minute.toString().padLeft(2, '0')}';
    return [
      const SizedBox(height: NordicSpace.md),
      const Icon(
        Icons.shield_outlined,
        size: 64.0,
        color: NordicColors.primary,
      ),
      const SizedBox(height: NordicSpace.md),
      Text(
        'Đã ghi nhận sự cố',
        textAlign: TextAlign.center,
        style: NordicTypography.h2.copyWith(color: NordicColors.primary),
      ),
      const SizedBox(height: NordicSpace.xs),
      const Text(
        'Ca làm của bạn đã được ghi nhận. Bạn được miễn phạt theo chính sách sự cố bất khả kháng.',
        textAlign: TextAlign.center,
        style: NordicTypography.bodyRegular,
      ),
      const SizedBox(height: NordicSpace.lg),

      NordicCard(
        isHighlighted: true,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            _row(
              'Miễn phạt',
              const StatusPill('Miễn phạt 100%', tone: PillTone.ok),
            ),
            const SizedBox(height: NordicSpace.sm),
            _row(
              'Tìm người làm thay (5 phút)',
              Text(
                incident.reDispatchStatus == 'SEARCHING'
                    ? 'Đang tìm thợ'
                    : incident.reDispatchStatus,
                style: NordicTypography.titleSmall,
              ),
            ),
            const SizedBox(height: NordicSpace.xs),
            _row(
              'Hạn tìm người thay',
              Text(deadline, style: NordicTypography.titleSmall),
            ),
          ],
        ),
      ),
    ];
  }
}
