import 'package:flutter/material.dart';

import '../../../core/device/camera_wrapper.dart';
import '../../../core/device/location_wrapper.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/bottom_action_bar.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/step_dots.dart';
import '../models/incident_log.dart';
import '../services/dispatch_service.dart';

/// Screen for reporting force majeure incidents with camera evidence and GPS (MOB-M3-04, BR-10).
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
      if (mounted) {
        setState(() {
          _errorMessage = 'Không thể chụp ảnh: $e';
        });
      }
    }
  }

  Future<void> _handleSubmitIncident() async {
    if (_capturedEvidence == null) {
      setState(() {
        _errorMessage = 'Vui lòng chụp ảnh bằng chứng hiện trường sự cố (bắt buộc theo BR-10)';
      });
      return;
    }

    if (_currentGps == null) {
      await _fetchLocation();
      if (_currentGps == null) {
        setState(() {
          _errorMessage = 'Không lấy được toạ độ GPS của sự cố';
        });
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
      setState(() {
        _isSubmitting = false;
        _errorMessage = 'Gửi báo cáo sự cố thất bại: $e';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Báo Cáo Sự Cố Bất Khả Kháng')),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: _submittedIncident != null
              ? _buildSubmittedView()
              : _buildFormView(),
        ),
      ),
      bottomNavigationBar: _submittedIncident != null
          ? null
          : BottomActionBar(
              children: [
                NordicButton(
                  key: const Key('submit_incident_button'),
                  label: 'Gửi Báo Cáo Sự Cố',
                  isLoading: _isSubmitting,
                  onPressed: _isSubmitting ? null : _handleSubmitIncident,
                ),
              ],
            ),
    );
  }

  static const _incidentIcons = {
    'ACCIDENT': Icons.car_crash_outlined,
    'HEALTH': Icons.medical_services_outlined,
    'SEVERE_WEATHER': Icons.thunderstorm_outlined,
    'OTHER': Icons.more_horiz,
  };

  /// `.kind`: one tile of the 2 x 2 grid of incident types, the chosen one with a primary border.
  Widget _kindTile(String code) {
    final on = _selectedType == code;
    return InkWell(
      key: Key('incident-kind-$code'),
      borderRadius: BorderRadius.zero,
      onTap: () => setState(() => _selectedType = code),
      child: Container(
        padding: EdgeInsets.all(on ? 13.0 : 14.0),
        decoration: BoxDecoration(
          color: NordicColors.surface,
          borderRadius: BorderRadius.zero,
          border: Border.all(
            color: on ? NordicColors.primary : NordicColors.border,
            width: on ? 2.0 : 1.0,
          ),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(_incidentIcons[code], color: NordicColors.primary),
            const SizedBox(height: 10.0),
            Text(
              _incidentTypes[code]!,
              style: NordicTypography.titleSmall,
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildFormView() {
    final codes = _incidentTypes.keys.toList();
    return ListView(
      padding: const EdgeInsets.fromLTRB(20.0, 8.0, 20.0, 24.0),
      children: [
        const Text(
          'Gặp sự cố trên đường / hiện trường?',
          style: NordicTypography.h2,
        ),
        const SizedBox(height: 4.0),
        const Text(
          'Báo cáo kèm ảnh và GPS để được miễn phạt. Hệ thống tự động kích hoạt điều phối cứu hộ 5 phút.',
          style: NordicTypography.bodySmall,
        ),
        const SizedBox(height: 16.0),

        // Incident type: 2 x 2 tiles
        Row(
          children: [
            Expanded(child: _kindTile(codes[0])),
            const SizedBox(width: 12.0),
            Expanded(child: _kindTile(codes[1])),
          ],
        ),
        const SizedBox(height: 12.0),
        Row(
          children: [
            Expanded(child: _kindTile(codes[2])),
            const SizedBox(width: 12.0),
            Expanded(child: _kindTile(codes[3])),
          ],
        ),

        const SizedBox(height: 24.0),
        const Text('MÔ TẢ', style: NordicTypography.caps),
        const SizedBox(height: 8.0),
        TextField(
          key: const Key('incident_description_input'),
          controller: _descriptionController,
          maxLines: 3,
          decoration: const InputDecoration(
            hintText: 'Ví dụ: Xe bị thủng lốp tại ngã tư Cầu Giấy, đang tìm tiệm vá xe...',
          ),
        ),

        const SizedBox(height: 24.0),
        const Text('ẢNH VÀ VỊ TRÍ', style: NordicTypography.caps),
        const SizedBox(height: 8.0),
        // Camera evidence & GPS
        NordicCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              OutlinedButton.icon(
                key: const Key('take_incident_photo_button'),
                onPressed: _handleCapturePhoto,
                icon: const Icon(Icons.add_a_photo_outlined),
                label: Text(
                  _capturedEvidence != null
                      ? 'Chụp lại ảnh'
                      : 'Chụp ảnh bằng chứng',
                ),
                style: OutlinedButton.styleFrom(
                  padding: const EdgeInsets.symmetric(vertical: 14.0),
                ),
              ),
              if (_capturedEvidence != null) ...[
                const SizedBox(height: 8.0),
                Row(
                  children: [
                    const Icon(
                      Icons.check_circle,
                      color: NordicColors.primary,
                      size: 16.0,
                    ),
                    const SizedBox(width: 6.0),
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
              const SizedBox(height: 12.0),
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
                  const SizedBox(width: 8.0),
                  Expanded(
                    child: Text(
                      _isLoadingGps
                          ? 'Đang định vị GPS...'
                          : _currentGps != null
                          ? 'GPS: ${_currentGps!.latitude.toStringAsFixed(4)}, ${_currentGps!.longitude.toStringAsFixed(4)}'
                          : 'Chưa lấy được GPS',
                      style: NordicTypography.bodySmall,
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
        const SizedBox(height: 16.0),

        // What happens next
        const NordicCard(
          child: StepDots(
            steps: [
              StepItem('Báo', DotState.current),
              StepItem('Tìm thợ thay', DotState.todo),
              StepItem('Thợ mới', DotState.todo),
            ],
          ),
        ),

        if (_errorMessage != null) ...[
          const SizedBox(height: 16.0),
          Container(
            padding: const EdgeInsets.all(12.0),
            decoration: BoxDecoration(
              color: NordicColors.errorContainer,
              borderRadius: BorderRadius.zero,
            ),
            child: Text(
              _errorMessage!,
              style: NordicTypography.bodySmall.copyWith(
                color: NordicColors.error,
              ),
            ),
          ),
        ],
      ],
    );
  }

  Widget _buildSubmittedView() {
    final incident = _submittedIncident!;

    return ListView(
      padding: const EdgeInsets.all(24.0),
      children: [
        const Icon(
          Icons.shield_outlined,
          size: 64.0,
          color: NordicColors.primary,
        ),
        const SizedBox(height: 16.0),
        Text(
          'Đã Ghi Nhận Sự Cố Bất Khả Kháng',
          textAlign: TextAlign.center,
          style: NordicTypography.h2.copyWith(color: NordicColors.primary),
        ),
        const SizedBox(height: 8.0),
        Text(
          'Ca làm #${widget.assignmentId} đã được ghi nhận. Bạn được MIỄN PHẠT theo chính sách BR-10.',
          textAlign: TextAlign.center,
          style: NordicTypography.bodyRegular,
        ),
        const SizedBox(height: 24.0),

        NordicCard(
          isHighlighted: true,
          child: Padding(
            padding: const EdgeInsets.all(16.0),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    const Expanded(
                      child: Text(
                        'Trạng thái miễn phạt:',
                        style: NordicTypography.bodySmall,
                      ),
                    ),
                    Container(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 8.0,
                        vertical: 4.0,
                      ),
                      decoration: BoxDecoration(
                        color: NordicColors.success.withAlpha(20),
                        borderRadius: BorderRadius.zero,
                      ),
                      child: Text(
                        'MIỄN PHẠT 100%',
                        style: NordicTypography.labelSmall.copyWith(
                          color: NordicColors.success,
                        ),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 12.0),
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    const Expanded(
                      child: Text(
                        'Điều phối lại (5 phút):',
                        style: NordicTypography.bodySmall,
                      ),
                    ),
                    Text(
                      incident.reDispatchStatus == 'SEARCHING'
                          ? 'Đang tìm thợ'
                          : incident.reDispatchStatus,
                      style: NordicTypography.labelMedium,
                    ),
                  ],
                ),
                const SizedBox(height: 8.0),
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    const Expanded(
                      child: Text(
                        'Hạn quét cứu hộ:',
                        style: NordicTypography.bodySmall,
                      ),
                    ),
                    Text(
                      '${incident.reDispatchDeadline.hour.toString().padLeft(2, '0')}:${incident.reDispatchDeadline.minute.toString().padLeft(2, '0')}',
                      style: NordicTypography.labelMedium,
                    ),
                  ],
                ),
              ],
            ),
          ),
        ),
        const SizedBox(height: 24.0),

        NordicButton(
          key: const Key('finish_incident_button'),
          label: 'Đã Hiểu',
          onPressed: () {
            Navigator.of(context).maybePop();
          },
        ),
      ],
    );
  }
}
