import 'package:flutter/material.dart';

import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/bottom_action_bar.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_text_input.dart';
import '../../../core/widgets/status_pill.dart';
import '../models/customer_acceptance_models.dart';
import '../models/job_photo_models.dart';
import '../services/customer_acceptance_service.dart';
import '../services/workers_photos_service.dart';
import '../widgets/job_photo_image.dart';

/// Arguments of the acceptance route: the assignment whose finished job the customer looks at. The order screen passes it.
class CustomerAcceptanceArgs {
  final int assignmentId;

  const CustomerAcceptanceArgs({required this.assignmentId});
}

/// Screen for Customer to view Before/After photos and confirm acceptance or request redo (MOB-M4-05, contract workers.md §2.5).
///
/// It shows the real photos and the real answer of the server: a failed call is an error with a retry, never a made-up success.
class CustomerAcceptanceScreen extends StatefulWidget {
  final int assignmentId;
  final CustomerAcceptanceService? acceptanceService;
  final WorkersPhotosService? photosService;

  const CustomerAcceptanceScreen({
    super.key,
    required this.assignmentId,
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
        _successMessage = 'Nghiệm thu thành công! Ca làm việc đã hoàn tất.';
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
        _errorMessage = 'Vui lòng nhập lý do yêu cầu dọn lại.';
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
            'Đã gửi yêu cầu dọn lại (15–30 ph). Thợ đã được thông báo bổ sung.';
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
    return Scaffold(
      appBar: AppBar(title: Text('Nghiệm Thu Ca #${widget.assignmentId}')),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: ListView(
            padding: const EdgeInsets.fromLTRB(20.0, 8.0, 20.0, 24.0),
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
                        const SizedBox(height: 4.0),
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
              const SizedBox(height: 20.0),
              const Text(
                'Đối Chiếu Ảnh Before / After',
                style: NordicTypography.titleSmall,
              ),
              const SizedBox(height: 12.0),

              _buildAngleSelector(),
              const SizedBox(height: 12.0),

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
                const SizedBox(height: 12.0),
              ] else if (_loadingPhotos) ...[
                const Padding(
                  padding: EdgeInsets.symmetric(vertical: 12.0),
                  child: Center(child: CircularProgressIndicator()),
                ),
              ],
              _buildPhotoComparisonCard(),
              const SizedBox(height: 16.0),

              if (_errorMessage != null) ...[
                _notice(message: _errorMessage!, isError: true),
                const SizedBox(height: 16.0),
              ],
              if (_successMessage != null) ...[
                _notice(message: _successMessage!, isError: false),
                const SizedBox(height: 16.0),
              ],

              if (_completionResult != null) ...[
                _buildCompletionSummaryCard(),
                const SizedBox(height: 16.0),
              ],

              if (_status != 'COMPLETED') ...[
                if (_showRedoForm)
                  _buildRedoFormCard()
                else
                  NordicTextInput(
                    label: 'Phản hồi (không bắt buộc)',
                    controller: _feedbackController,
                  ),
              ],
            ],
          ),
        ),
      ),
      // ui-templates `.btn-bar`: "Dọn lại" beside the main "Nghiệm thu".
      bottomNavigationBar: awaiting
          ? BottomActionBar(
              children: [
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
              ],
            )
          : null,
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
        borderRadius: BorderRadius.circular(12.0),
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
              const SizedBox(width: 8.0),
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
          if (action != null) ...[const SizedBox(height: 8.0), action],
        ],
      ),
    );
  }

  Widget _buildStatusChip() {
    final (label, tone) = switch (_status) {
      'COMPLETED' => ('ĐÃ NGHIỆM THU', PillTone.ok),
      'IN_PROGRESS' => ('ĐANG DỌN LẠI', PillTone.neutral),
      _ => ('CHỜ NGHIỆM THU', PillTone.warn),
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
                label: 'TRƯỚC (Before)',
                photo: beforePhoto,
                volScore: beforePhoto?.volScore,
                tint: NordicColors.surfaceDim,
              ),
            ),
            const SizedBox(width: 8.0),
            Expanded(
              child: _buildPhotoBox(
                label: 'SAU (After)',
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
    return ClipRRect(
      borderRadius: BorderRadius.circular(12.0),
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
            SizedBox(
              height: 120.0,
              child: JobPhotoImage(
                service: _photosService,
                photo: photo,
                height: 120.0,
                width: double.infinity,
              ),
            ),
            if (volScore != null)
              Padding(
                padding: const EdgeInsets.symmetric(
                  vertical: 4.0,
                  horizontal: 6.0,
                ),
                child: Text(
                  'VoL: ${volScore.toStringAsFixed(1)} (ĐẠT)',
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
              SizedBox(width: 8.0),
              Flexible(
                child: Text(
                  'Chi Tiết Nghiệm Thu & Khóa Ca',
                  style: NordicTypography.titleSmall,
                ),
              ),
            ],
          ),
          const Divider(),
          _buildDetailRow('Tổng Phí Dịch Vụ:', '${gross.toString()} đ'),
          _buildDetailRow(
            'Chi Phí Nền Tảng ($commissionPercent%):',
            '${(gross - payout).toString()} đ',
          ),
          const Divider(),
          _buildDetailRow(
            'Thu Nhập Thợ (${100 - commissionPercent}%):',
            '${payout.toString()} đ',
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
          const SizedBox(width: 8.0),
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
            'Yêu Cầu Dọn Lại (15–30 phút)',
            style: NordicTypography.titleSmall,
          ),
          const SizedBox(height: 4.0),
          const Text(
            'Ghi rõ khu vực thợ cần làm lại.',
            style: NordicTypography.bodySmall,
          ),
          const SizedBox(height: 12.0),
          NordicTextInput(
            label: 'Nội dung / Lý do yêu cầu dọn lại',
            controller: _redoReasonController,
          ),
          const SizedBox(height: 10.0),
          NordicTextInput(
            label: 'Đường dẫn ảnh khoanh vùng lỗi (Không bắt buộc)',
            controller: _redoPhotoController,
          ),
          const SizedBox(height: 14.0),
          Row(
            children: [
              Expanded(
                child: NordicButton(
                  label: 'Hủy Bỏ',
                  variant: NordicButtonVariant.secondary,
                  onPressed: () => setState(() => _showRedoForm = false),
                ),
              ),
              const SizedBox(width: 10.0),
              Expanded(
                child: NordicButton(
                  label: 'Gửi Yêu Cầu Dọn Lại',
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
