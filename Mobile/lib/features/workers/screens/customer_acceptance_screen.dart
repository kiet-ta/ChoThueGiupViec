import 'package:flutter/material.dart';

import '../../../core/network/api_client.dart';
import '../../../core/motion/error_shake.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_text_input.dart';
import '../../../core/widgets/pressable.dart';
import '../../../core/widgets/status_pill.dart';
import '../../booking/logic/booking_logic.dart' show formatVnd;
import '../models/customer_acceptance_models.dart';
import '../models/job_photo_models.dart';
import '../services/customer_acceptance_service.dart';
import '../services/workers_photos_service.dart';
import '../widgets/job_photo_image.dart';

class _AcceptancePhotoViewer extends StatelessWidget {
  final String label;
  final String url;
  final Map<String, String> headers;

  const _AcceptancePhotoViewer({
    required this.label,
    required this.url,
    required this.headers,
  });

  @override
  Widget build(BuildContext context) {
    final height =
        MediaQuery.sizeOf(context).height -
        MediaQuery.paddingOf(context).top -
        kToolbarHeight -
        NordicSpace.xl;
    return NordicPage.pushed(
      title: 'Ảnh $label',
      children: [
        SizedBox(
          height: height.clamp(0.0, double.infinity),
          child: InteractiveViewer(
            minScale: 1,
            maxScale: 4,
            child: Center(
              child: Image.network(
                url,
                headers: headers,
                fit: BoxFit.contain,
                errorBuilder: (_, _, _) => const Text('Không tải được ảnh'),
              ),
            ),
          ),
        ),
      ],
    );
  }
}

/// Arguments of the acceptance route: the assignment whose finished job the customer looks at. The order screen passes it.
class CustomerAcceptanceArgs {
  final int assignmentId;
  final bool completed;

  const CustomerAcceptanceArgs({
    required this.assignmentId,
    this.completed = false,
  });
}

/// Screen for Customer to view Before/After photos and confirm acceptance or request redo (MOB-M4-05, contract workers.md §2.5).
///
/// It shows the real photos and the real answer of the server: a failed call is an error with a retry, never a made-up success.
class CustomerAcceptanceScreen extends StatefulWidget {
  final int assignmentId;
  final bool completed;
  final CustomerAcceptanceService? acceptanceService;
  final WorkersPhotosService? photosService;

  const CustomerAcceptanceScreen({
    super.key,
    required this.assignmentId,
    this.completed = false,
    this.acceptanceService,
    this.photosService,
  });

  @override
  State<CustomerAcceptanceScreen> createState() =>
      _CustomerAcceptanceScreenState();
}

class _CustomerAcceptanceScreenState extends State<CustomerAcceptanceScreen> {
  late final CustomerAcceptanceService _acceptanceService;
  late final WorkersPhotosService _photosService;

  String _status =
      'AWAITING_ACCEPTANCE'; // AWAITING_ACCEPTANCE | COMPLETED | IN_PROGRESS
  int _selectedAngle = 1;
  bool _isLoading = false;
  bool _loadingPhotos = true;
  String? _photosError;
  bool _showRedoForm = false;

  /// Empty redo requests so far: each one shakes the form (DESIGN-SYSTEM §5 Form).
  int _redoErrors = 0;
  String? _errorMessage;
  String? _successMessage;

  final _feedbackController = TextEditingController();
  final _redoReasonController = TextEditingController();
  final _redoPhotoController = TextEditingController();

  AssignmentCompletionDto? _completionResult;
  final List<JobPhotoDto> _photos = [];

  static const List<Map<String, String>> _angleTitles = [
    {'angle': '1', 'name': 'Góc 1: Toàn cảnh phòng'},
    {'angle': '2', 'name': 'Góc 2: Bàn ghế & tủ kệ'},
    {'angle': '3', 'name': 'Góc 3: Cửa kính & rèm'},
    {'angle': '4', 'name': 'Góc 4: Mặt sàn & thảm'},
    {'angle': '5', 'name': 'Góc 5: Bồn rửa & khu bếp'},
  ];

  @override
  void initState() {
    super.initState();
    if (widget.completed) _status = 'COMPLETED';
    _acceptanceService =
        widget.acceptanceService ?? CustomerAcceptanceService();
    _photosService = widget.photosService ?? WorkersPhotosService();
    _loadPhotos();
  }

  @override
  void dispose() {
    _feedbackController.dispose();
    _redoReasonController.dispose();
    _redoPhotoController.dispose();
    super.dispose();
  }

  Future<void> _loadPhotos() async {
    setState(() {
      _loadingPhotos = true;
      _photosError = null;
    });
    try {
      final response = await _photosService.getPhotos(widget.assignmentId);
      if (!mounted) return;
      setState(() {
        _photos
          ..clear()
          ..addAll(response.data ?? const <JobPhotoDto>[]);
        _loadingPhotos = false;
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _loadingPhotos = false;
        _photosError = _failure(e, 'Không tải được ảnh của ca này.');
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _loadingPhotos = false;
        _photosError = 'Không tải được ảnh của ca này.';
      });
    }
  }

  /// What the worker of a failed call is told: the server's own words for a network failure, a fixed text for the states.
  String _failure(ApiException e, String fallback) {
    if (e.isNetworkError) return e.message;
    if (e.isNotFound || e.isForbidden) {
      return 'Không tìm thấy ca làm này trong đơn của bạn.';
    }
    if (e.isConflict) {
      return 'Ca này không còn chờ nghiệm thu. Hãy quay lại chi tiết đơn để xem trạng thái mới.';
    }
    return e.message.isNotEmpty ? e.message : fallback;
  }

  void _fail(String message) {
    if (!mounted) return;
    setState(() {
      _isLoading = false;
      _errorMessage = message;
      _successMessage = null;
    });
  }

  Future<void> _handleAcceptCompletion() async {
    setState(() {
      _isLoading = true;
      _errorMessage = null;
      _successMessage = null;
    });

    try {
      final response = await _acceptanceService.acceptCompletion(
        assignmentId: widget.assignmentId,
        feedback: _feedbackController.text.trim(),
      );
      final done = response.data;
      if (done == null) {
        _fail('Máy chủ không trả kết quả nghiệm thu. Vui lòng thử lại.');
        return;
      }
      if (!mounted) return;
      setState(() {
        _isLoading = false;
        _completionResult = done;
        _status = done.status;
        _successMessage = 'Đã nghiệm thu. Ca làm đã xong.';
      });
    } on ApiException catch (e) {
      _fail(_failure(e, 'Không nghiệm thu được. Vui lòng thử lại.'));
    } catch (_) {
      _fail('Không nghiệm thu được. Vui lòng thử lại.');
    }
  }

  Future<void> _handleRequestRedo() async {
    final reason = _redoReasonController.text.trim();
    if (reason.isEmpty) {
      setState(() {
        _errorMessage = 'Nhập chỗ cần làm lại.';
        _redoErrors++;
        _successMessage = null;
      });
      return;
    }

    setState(() {
      _isLoading = true;
      _errorMessage = null;
      _successMessage = null;
    });

    try {
      final response = await _acceptanceService.requestRedo(
        assignmentId: widget.assignmentId,
        request: RequestRedoRequest(
          reason: reason,
          redoPhotoUrl: _redoPhotoController.text.trim(),
        ),
      );
      final updated = response.data;
      if (updated == null) {
        _fail('Máy chủ không trả kết quả yêu cầu dọn lại. Vui lòng thử lại.');
        return;
      }
      if (!mounted) return;
      setState(() {
        _isLoading = false;
        _status = updated.status;
        _showRedoForm = false;
        _successMessage =
            'Đã gửi yêu cầu dọn lại (15–30 phút). Người làm đã được báo.';
      });
    } on ApiException catch (e) {
      _fail(_failure(e, 'Không gửi được yêu cầu dọn lại. Vui lòng thử lại.'));
    } catch (_) {
      _fail('Không gửi được yêu cầu dọn lại. Vui lòng thử lại.');
    }
  }

  JobPhotoDto? _getPhoto(String phase, int angle) {
    try {
      return _photos.firstWhere(
        (p) => p.photoPhase == phase && p.angleNo == angle,
      );
    } catch (_) {
      return null;
    }
  }

  /// Angles that have both a BEFORE and an AFTER photo, out of 5 (ui-templates `customer/acceptance.html`).
  int get _pairedAngles => [
    for (var a = 1; a <= 5; a++)
      if (_getPhoto('BEFORE', a) != null && _getPhoto('AFTER', a) != null) a,
  ].length;

  @override
  Widget build(BuildContext context) {
    final awaiting = _status != 'COMPLETED' && !_showRedoForm;
    // MOB-UI-16: a pushed `NordicPage` titled in words; "Dọn lại" beside the main "Nghiệm thu" in the bar.
    return NordicPage.pushed(
      title: 'Nghiệm thu ca',
      actions: awaiting
          ? [
              NordicButton(
                label: 'Dọn lại',
                variant: NordicButtonVariant.secondary,
                onPressed: () {
                  setState(() {
                    _showRedoForm = true;
                    _errorMessage = null;
                  });
                },
              ),
              NordicButton(
                label: 'Nghiệm thu',
                variant: NordicButtonVariant.primary,
                isLoading: _isLoading,
                onPressed: _handleAcceptCompletion,
              ),
            ]
          : null,
      children: [
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    _loadingPhotos ? '–/5' : '$_pairedAngles/5',
                    key: const Key('acceptance-paired'),
                    style: NordicTypography.numberMedium,
                  ),
                  const SizedBox(height: NordicSpace.xxs),
                  const Text(
                    'Góc có ảnh trước & sau',
                    style: NordicTypography.bodySmall,
                  ),
                ],
              ),
            ),
            _buildStatusChip(),
          ],
        ),
        const SizedBox(height: NordicSpace.gutter),
        const Text(
          'So sánh ảnh trước và sau',
          style: NordicTypography.titleSmall,
        ),
        const SizedBox(height: NordicSpace.sm),

        _buildAngleSelector(),
        const SizedBox(height: NordicSpace.sm),

        if (_photosError != null) ...[
          _notice(
            key: const Key('photos-error'),
            message: _photosError!,
            isError: true,
            action: NordicButton(
              key: const Key('photos-retry'),
              label: 'Thử lại',
              variant: NordicButtonVariant.secondary,
              onPressed: _loadPhotos,
            ),
          ),
          const SizedBox(height: NordicSpace.sm),
        ] else if (_loadingPhotos) ...[
          const Padding(
            padding: EdgeInsets.symmetric(vertical: 12.0),
            child: Center(child: CircularProgressIndicator()),
          ),
        ],
        _buildPhotoComparisonCard(),
        const SizedBox(height: NordicSpace.md),

        if (_errorMessage != null) ...[
          _notice(message: _errorMessage!, isError: true),
          const SizedBox(height: NordicSpace.md),
        ],
        if (_successMessage != null) ...[
          _notice(message: _successMessage!, isError: false),
          const SizedBox(height: NordicSpace.md),
        ],

        if (_completionResult != null) ...[
          _buildCompletionSummaryCard(),
          const SizedBox(height: NordicSpace.md),
        ],
        if (widget.completed && _completionResult == null) ...[
          const Text(
            'Ca này đã được nghiệm thu. Chi tiết số tiền chưa có để xem lại.',
            style: NordicTypography.bodySmall,
          ),
          const SizedBox(height: NordicSpace.md),
        ],

        if (_status != 'COMPLETED') ...[
          if (_showRedoForm)
            ErrorShake(errors: _redoErrors, child: _buildRedoFormCard())
          else
            NordicTextInput(
              label: 'Phản hồi (không bắt buộc)',
              controller: _feedbackController,
            ),
        ],
      ],
    );
  }

  /// An error (or success) line in the tokens' error / primary-fixed tints.
  Widget _notice({
    Key? key,
    required String message,
    required bool isError,
    Widget? action,
  }) {
    final foreground = isError ? NordicColors.error : NordicColors.primary;
    return Container(
      key: key,
      padding: const EdgeInsets.all(12.0),
      decoration: BoxDecoration(
        color: isError
            ? NordicColors.errorContainer
            : NordicColors.primaryFixed,
        borderRadius: NordicRadius.tileAll,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(
                isError ? Icons.error_outline : Icons.check_circle_outline,
                color: foreground,
              ),
              const SizedBox(width: NordicSpace.xs),
              Expanded(
                child: Text(
                  message,
                  style: TextStyle(
                    color: foreground,
                    fontSize: 13.0,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ),
            ],
          ),
          if (action != null) ...[
            const SizedBox(height: NordicSpace.xs),
            action,
          ],
        ],
      ),
    );
  }

  Widget _buildStatusChip() {
    final (label, tone) = switch (_status) {
      'COMPLETED' => ('Đã nghiệm thu', PillTone.ok),
      'IN_PROGRESS' => ('Đang dọn lại', PillTone.neutral),
      _ => ('Chờ nghiệm thu', PillTone.warn),
    };
    return StatusPill(label, tone: tone, dot: true);
  }

  Widget _buildAngleSelector() {
    return SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      child: Row(
        children: [
          for (int i = 1; i <= 5; i++)
            Padding(
              padding: const EdgeInsets.only(right: 8.0),
              child: ChoiceChip(
                label: Text('Góc $i'),
                selected: _selectedAngle == i,
                showCheckmark: false,
                onSelected: (selected) {
                  if (selected) {
                    setState(() => _selectedAngle = i);
                  }
                },
              ),
            ),
        ],
      ),
    );
  }

  Widget _buildPhotoComparisonCard() {
    final beforePhoto = _getPhoto('BEFORE', _selectedAngle);
    final afterPhoto = _getPhoto('AFTER', _selectedAngle);
    final title = _angleTitles.firstWhere(
      (a) => a['angle'] == '$_selectedAngle',
      orElse: () => {'name': 'Góc $_selectedAngle'},
    )['name'];

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          title!,
          style: const TextStyle(fontSize: 12.0, fontWeight: FontWeight.w600),
        ),
        const SizedBox(height: 6.0),
        Row(
          children: [
            Expanded(
              child: _buildPhotoBox(
                label: 'Trước',
                photo: beforePhoto,
                volScore: beforePhoto?.volScore,
                tint: NordicColors.surfaceDim,
              ),
            ),
            const SizedBox(width: NordicSpace.xs),
            Expanded(
              child: _buildPhotoBox(
                label: 'Sau',
                photo: afterPhoto,
                volScore: afterPhoto?.volScore,
                tint: NordicColors.primaryFixed,
              ),
            ),
          ],
        ),
      ],
    );
  }

  Widget _buildPhotoBox({
    required String label,
    required JobPhotoDto? photo,
    double? volScore,
    required Color tint,
  }) {
    final source = photo == null
        ? null
        : _photosService.photoImage(photo.photoUrl);
    return ClipRRect(
      borderRadius: NordicRadius.tileAll,
      child: Container(
        color: NordicColors.surfaceLow,
        child: Column(
          children: [
            Container(
              width: double.infinity,
              color: tint,
              padding: const EdgeInsets.symmetric(
                vertical: 6.0,
                horizontal: 8.0,
              ),
              child: Text(
                label,
                textAlign: TextAlign.center,
                style: const TextStyle(
                  fontSize: 11.0,
                  fontWeight: FontWeight.w600,
                  color: NordicColors.textTitle,
                ),
              ),
            ),
            Semantics(
              button: source != null,
              label: source == null
                  ? '$label: không có ảnh để xem'
                  : 'Xem ảnh $label toàn màn hình',
              child: Pressable(
                enabled: source != null,
                child: GestureDetector(
                  key: photo == null
                      ? null
                      : Key('open-photo-${photo.photoId}'),
                  onTap: source == null
                      ? null
                      : () => Navigator.of(context).push(
                          MaterialPageRoute<void>(
                            builder: (_) => _AcceptancePhotoViewer(
                              label: label,
                              url: source.url,
                              headers: source.headers,
                            ),
                          ),
                        ),
                  child: SizedBox(
                    height: 120.0,
                    child: JobPhotoImage(
                      service: _photosService,
                      photo: photo,
                      height: 120.0,
                      width: double.infinity,
                    ),
                  ),
                ),
              ),
            ),
            if (volScore != null)
              Padding(
                padding: const EdgeInsets.symmetric(
                  vertical: 4.0,
                  horizontal: 6.0,
                ),
                child: Text(
                  'Ảnh rõ nét · ${volScore.toStringAsFixed(1).replaceAll('.', ',')}',
                  style: const TextStyle(
                    fontSize: 10.0,
                    color: NordicColors.textSecondary,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }

  Widget _buildCompletionSummaryCard() {
    final result = _completionResult;
    if (result == null) return const SizedBox.shrink();
    final gross = result.grossAmount;
    final payout = result.payoutAmount;
    final commissionPercent = (result.commissionRate * 100).round();

    return NordicCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Row(
            children: [
              Icon(Icons.verified_outlined, color: NordicColors.primary),
              SizedBox(width: NordicSpace.xs),
              Flexible(
                child: Text(
                  'Chi tiết nghiệm thu',
                  style: NordicTypography.titleSmall,
                ),
              ),
            ],
          ),
          const Divider(),
          _buildDetailRow('Tổng tiền dịch vụ', formatVnd(gross)),
          _buildDetailRow(
            'Phí nền tảng ($commissionPercent%)',
            formatVnd(gross - payout),
          ),
          const Divider(),
          _buildDetailRow(
            'Người làm nhận (${100 - commissionPercent}%)',
            formatVnd(payout),
            isBold: true,
          ),
        ],
      ),
    );
  }

  Widget _buildDetailRow(String title, String val, {bool isBold = false}) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4.0),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Expanded(
            child: Text(
              title,
              style: isBold
                  ? NordicTypography.labelMedium
                  : NordicTypography.bodySmall,
            ),
          ),
          const SizedBox(width: NordicSpace.xs),
          Flexible(
            child: Text(
              val,
              textAlign: TextAlign.end,
              style: isBold
                  ? NordicTypography.titleSmall
                  : NordicTypography.labelMedium,
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildRedoFormCard() {
    return NordicCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text(
            'Yêu cầu dọn lại (15–30 phút)',
            style: NordicTypography.titleSmall,
          ),
          const SizedBox(height: NordicSpace.xxs),
          const Text(
            'Ghi rõ chỗ chưa sạch để người làm quay lại làm.',
            style: NordicTypography.bodySmall,
          ),
          const SizedBox(height: NordicSpace.sm),
          NordicTextInput(
            label: 'Chỗ cần làm lại',
            controller: _redoReasonController,
          ),
          const SizedBox(height: 10.0),
          NordicTextInput(
            label: 'Link ảnh chỗ chưa sạch (không bắt buộc)',
            controller: _redoPhotoController,
          ),
          const SizedBox(height: 14.0),
          Row(
            children: [
              Expanded(
                child: NordicButton(
                  label: 'Huỷ',
                  variant: NordicButtonVariant.secondary,
                  onPressed: () => setState(() => _showRedoForm = false),
                ),
              ),
              const SizedBox(width: 10.0),
              Expanded(
                child: NordicButton(
                  label: 'Gửi yêu cầu',
                  variant: NordicButtonVariant.primary,
                  isLoading: _isLoading,
                  onPressed: _handleRequestRedo,
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}
