import 'package:flutter/material.dart';

import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/eyebrow_badge.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_text_input.dart';
import '../models/customer_acceptance_models.dart';
import '../models/job_photo_models.dart';
import '../services/customer_acceptance_service.dart';
import '../services/workers_photos_service.dart';
import '../widgets/job_photo_image.dart';
import '../widgets/tinted_card.dart';

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

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(
        title: Text('Nghiệm Thu Ca #${widget.assignmentId}'),
        backgroundColor: Colors.white,
        foregroundColor: NordicColors.textTitle,
        elevation: 0.5,
      ),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: ListView(
            padding: const EdgeInsets.all(16.0),
            children: [
              Wrap(
                alignment: WrapAlignment.spaceBetween,
                crossAxisAlignment: WrapCrossAlignment.center,
                spacing: 8.0,
                runSpacing: 8.0,
                children: [
                  const EyebrowBadge(text: 'Khách Hàng Nghiệm Thu'),
                  _buildStatusChip(),
                ],
              ),
              const SizedBox(height: 8.0),
              const Text(
                'Đối Chiếu Ảnh Before / After',
                style: NordicTypography.h2,
              ),
              const SizedBox(height: 6.0),
              const Text(
                'Vui lòng kiểm tra kỹ chất lượng vệ sinh theo từng góc chụp trước khi xác nhận nghiệm thu.',
                style: NordicTypography.bodySmall,
              ),
              const SizedBox(height: 16.0),

              // Angle selector
              _buildAngleSelector(),
              const SizedBox(height: 16.0),

              // Side-by-side Photo Comparison Card
              if (_photosError != null) ...[
                TintedCard(
                  key: const Key('photos-error'),
                  backgroundColor: const Color(0xFFFEE2E2),
                  child: Padding(
                    padding: const EdgeInsets.all(12.0),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          _photosError!,
                          style: const TextStyle(
                            color: Color(0xFF991B1B),
                            fontSize: 13.0,
                          ),
                        ),
                        const SizedBox(height: 8.0),
                        NordicButton(
                          key: const Key('photos-retry'),
                          label: 'Thử lại',
                          variant: NordicButtonVariant.secondary,
                          onPressed: _loadPhotos,
                        ),
                      ],
                    ),
                  ),
                ),
                const SizedBox(height: 16.0),
              ] else if (_loadingPhotos) ...[
                const Padding(
                  padding: EdgeInsets.symmetric(vertical: 12.0),
                  child: Center(child: CircularProgressIndicator()),
                ),
              ],
              _buildPhotoComparisonCard(),
              const SizedBox(height: 16.0),

              // Error or Success Alerts
              if (_errorMessage != null) ...[
                TintedCard(
                  backgroundColor: const Color(0xFFFEE2E2),
                  child: Padding(
                    padding: const EdgeInsets.all(12.0),
                    child: Row(
                      children: [
                        const Icon(
                          Icons.error_outline,
                          color: Color(0xFFDC2626),
                        ),
                        const SizedBox(width: 8.0),
                        Expanded(
                          child: Text(
                            _errorMessage!,
                            style: const TextStyle(
                              color: Color(0xFF991B1B),
                              fontSize: 13.0,
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
                const SizedBox(height: 16.0),
              ],
              if (_successMessage != null) ...[
                TintedCard(
                  backgroundColor: const Color(0xFFDCFCE7),
                  child: Padding(
                    padding: const EdgeInsets.all(12.0),
                    child: Row(
                      children: [
                        const Icon(
                          Icons.check_circle_outline,
                          color: Color(0xFF16A34A),
                        ),
                        const SizedBox(width: 8.0),
                        Expanded(
                          child: Text(
                            _successMessage!,
                            style: const TextStyle(
                              color: Color(0xFF15803D),
                              fontSize: 13.0,
                              fontWeight: FontWeight.bold,
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
                const SizedBox(height: 16.0),
              ],

              // Completed Payout Breakdown
              if (_completionResult != null) ...[
                _buildCompletionSummaryCard(),
                const SizedBox(height: 16.0),
              ],

              // Acceptance Actions / Redo Form
              if (_status != 'COMPLETED') ...[
                if (_showRedoForm) ...[
                  _buildRedoFormCard(),
                ] else ...[
                  NordicTextInput(
                    label: 'Đánh giá / Phản hồi (Không bắt buộc)',
                    controller: _feedbackController,
                  ),
                  const SizedBox(height: 12.0),
                  Row(
                    children: [
                      Expanded(
                        child: NordicButton(
                          label: 'Dọn Lại (15–30ph)',
                          variant: NordicButtonVariant.secondary,
                          onPressed: () {
                            setState(() {
                              _showRedoForm = true;
                              _errorMessage = null;
                            });
                          },
                        ),
                      ),
                      const SizedBox(width: 10.0),
                      Expanded(
                        child: NordicButton(
                          label: 'Xác Nhận Nghiệm Thu',
                          variant: NordicButtonVariant.primary,
                          isLoading: _isLoading,
                          onPressed: _handleAcceptCompletion,
                        ),
                      ),
                    ],
                  ),
                ],
              ],
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildStatusChip() {
    Color bg = const Color(0xFFFEF3C7);
    Color textClr = const Color(0xFFD97706);
    String label = 'CHỜ NGHIỆM THU';

    if (_status == 'COMPLETED') {
      bg = const Color(0xFFDCFCE7);
      textClr = const Color(0xFF16A34A);
      label = 'ĐÃ NGHIỆM THU';
    } else if (_status == 'IN_PROGRESS') {
      bg = const Color(0xFFE0F2FE);
      textClr = const Color(0xFF0284C7);
      label = 'ĐANG DỌN LẠI';
    }

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10.0, vertical: 4.0),
      decoration: BoxDecoration(
        color: bg,
        borderRadius: BorderRadius.circular(12.0),
      ),
      child: Text(
        label,
        style: TextStyle(
          color: textClr,
          fontWeight: FontWeight.bold,
          fontSize: 11.0,
        ),
      ),
    );
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
                selectedColor: NordicColors.primary,
                labelStyle: TextStyle(
                  color: _selectedAngle == i
                      ? Colors.white
                      : NordicColors.textTitle,
                  fontWeight: FontWeight.bold,
                ),
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

    return NordicCard(
      child: Padding(
        padding: const EdgeInsets.all(16.0),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(title!, style: NordicTypography.h3),
            const SizedBox(height: 12.0),
            Row(
              children: [
                Expanded(
                  child: _buildPhotoBox(
                    label: 'TRƯỚC (Before)',
                    photo: beforePhoto,
                    volScore: beforePhoto?.volScore,
                    headerBg: const Color(0xFFF1F5F9),
                    headerTextColor: NordicColors.textTitle,
                  ),
                ),
                const SizedBox(width: 8.0),
                Expanded(
                  child: _buildPhotoBox(
                    label: 'SAU (After)',
                    photo: afterPhoto,
                    volScore: afterPhoto?.volScore,
                    headerBg: const Color(0xFFDCFCE7),
                    headerTextColor: const Color(0xFF15803D),
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildPhotoBox({
    required String label,
    required JobPhotoDto? photo,
    double? volScore,
    required Color headerBg,
    required Color headerTextColor,
  }) {
    return Container(
      decoration: BoxDecoration(
        color: NordicColors.surface,
        borderRadius: BorderRadius.circular(12.0),
        border: Border.all(color: NordicColors.borderVariant),
      ),
      child: Column(
        children: [
          Container(
            width: double.infinity,
            padding: const EdgeInsets.symmetric(vertical: 6.0, horizontal: 8.0),
            decoration: BoxDecoration(
              color: headerBg,
              borderRadius: const BorderRadius.vertical(
                top: Radius.circular(11.0),
              ),
            ),
            child: Text(
              label,
              textAlign: TextAlign.center,
              style: TextStyle(
                fontSize: 12.0,
                fontWeight: FontWeight.bold,
                color: headerTextColor,
              ),
            ),
          ),
          Container(
            height: 110.0,
            color: const Color(0xFF0F172A),
            child: JobPhotoImage(
              service: _photosService,
              photo: photo,
              height: 110.0,
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
                  fontWeight: FontWeight.bold,
                ),
              ),
            ),
        ],
      ),
    );
  }

  Widget _buildCompletionSummaryCard() {
    final result = _completionResult;
    if (result == null) return const SizedBox.shrink();
    final gross = result.grossAmount;
    final payout = result.payoutAmount;
    final commissionPercent = (result.commissionRate * 100).round();

    return TintedCard(
      backgroundColor: const Color(0xFFF0FDF4),
      child: Padding(
        padding: const EdgeInsets.all(16.0),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Row(
              children: [
                Icon(Icons.verified, color: Color(0xFF16A34A)),
                SizedBox(width: 8.0),
                Flexible(
                  child: Text(
                    'Chi Tiết Nghiệm Thu & Khóa Ca',
                    style: NordicTypography.h3,
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
                  ? NordicTypography.h3
                  : NordicTypography.labelMedium,
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildRedoFormCard() {
    return TintedCard(
      backgroundColor: const Color(0xFFFEF2F2),
      child: Padding(
        padding: const EdgeInsets.all(16.0),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              'Yêu Cầu Dọn Lại (15–30 phút)',
              style: NordicTypography.h3,
            ),
            const SizedBox(height: 6.0),
            const Text(
              'Ghi rõ các khu vực thợ cần làm lại để hoàn thiện chất lượng ca làm.',
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
      ),
    );
  }
}
