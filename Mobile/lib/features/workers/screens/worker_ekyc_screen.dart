import 'package:flutter/material.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/eyebrow_badge.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_text_input.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../models/ekyc_models.dart';
import '../services/workers_ekyc_service.dart';
import '../widgets/tinted_card.dart';

/// Screen for Worker eKYC Identity Verification (CCCD 2 mặt + selfie).
/// Implements MOB-M4-01 according to contract workers.md §2.2.
class WorkerEkycScreen extends StatefulWidget {
  final WorkersEkycService? ekycService;

  const WorkerEkycScreen({
    super.key,
    this.ekycService,
  });

  @override
  State<WorkerEkycScreen> createState() => _WorkerEkycScreenState();
}

class _WorkerEkycScreenState extends State<WorkerEkycScreen> {
  late final WorkersEkycService _service;

  final _frontCccdController = TextEditingController(
    text: 'https://storage.giupviec.local/ekyc/front_sample.jpg',
  );
  final _backCccdController = TextEditingController(
    text: 'https://storage.giupviec.local/ekyc/back_sample.jpg',
  );
  final _selfieController = TextEditingController(
    text: 'https://storage.giupviec.local/ekyc/selfie_sample.jpg',
  );

  int _currentStep = 0;
  bool _isLoading = false;
  String? _errorMessage;
  EkycResultResponse? _result;

  @override
  void initState() {
    super.initState();
    _service = widget.ekycService ?? WorkersEkycService();
  }

  @override
  void dispose() {
    _frontCccdController.dispose();
    _backCccdController.dispose();
    _selfieController.dispose();
    super.dispose();
  }

  Future<void> _submitEkyc() async {
    final frontUrl = _frontCccdController.text.trim();
    final backUrl = _backCccdController.text.trim();
    final selfieUrl = _selfieController.text.trim();

    if (frontUrl.isEmpty || backUrl.isEmpty || selfieUrl.isEmpty) {
      setState(() {
        _errorMessage = 'Vui lòng cung cấp đầy đủ 3 ảnh (CCCD mặt trước, mặt sau và selfie).';
      });
      return;
    }

    setState(() {
      _isLoading = true;
      _errorMessage = null;
    });

    try {
      final response = await _service.submitEkyc(
        SubmitEkycRequest(
          frontCccdUrl: frontUrl,
          backCccdUrl: backUrl,
          selfieUrl: selfieUrl,
        ),
      );

      setState(() {
        _isLoading = false;
        _result = response.data;
      });
    } catch (e) {
      setState(() {
        _isLoading = false;
        _errorMessage = e.toString();
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(
        title: const Text('Xác Thực eKYC'),
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
              const EyebrowBadge(text: 'Xác Thực Danh Tính Thợ'),
              const SizedBox(height: 8.0),
              const Text(
                'Chụp Ảnh CCCD & Chân Dung',
                style: NordicTypography.h2,
              ),
              const SizedBox(height: 6.0),
              const Text(
                'Theo quy định PRD 2.8, thợ Freelance cần xác thực eKYC trước khi nhận ca làm.',
                style: NordicTypography.bodySmall,
              ),
              const SizedBox(height: 16.0),

              if (_result != null) ...[
                _buildResultView(_result!),
              ] else ...[
                _buildStepIndicator(),
                const SizedBox(height: 16.0),
                if (_errorMessage != null) ...[
                  TintedCard(
                    backgroundColor: const Color(0xFFFEE2E2),
                    child: Padding(
                      padding: const EdgeInsets.all(12.0),
                      child: Text(
                        _errorMessage!,
                        style: const TextStyle(color: Color(0xFF991B1B), fontSize: 14.0),
                      ),
                    ),
                  ),
                  const SizedBox(height: 16.0),
                ],
                _buildStepContent(),
                const SizedBox(height: 20.0),
                _buildActionButtons(),
              ],
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildStepIndicator() {
    return Row(
      children: [
        _buildStepBadge(0, '1. Mặt trước'),
        const SizedBox(width: 4.0),
        _buildStepBadge(1, '2. Mặt sau'),
        const SizedBox(width: 4.0),
        _buildStepBadge(2, '3. Selfie'),
        const SizedBox(width: 4.0),
        _buildStepBadge(3, '4. Xác nhận'),
      ],
    );
  }

  Widget _buildStepBadge(int stepIndex, String title) {
    final isActive = _currentStep == stepIndex;
    final isDone = _currentStep > stepIndex;

    return Expanded(
      child: Container(
        padding: const EdgeInsets.symmetric(vertical: 8.0, horizontal: 4.0),
        decoration: BoxDecoration(
          color: isActive
              ? NordicColors.primary
              : isDone
                  ? NordicColors.primary.withValues(alpha: 0.15)
                  : NordicColors.surface,
          borderRadius: BorderRadius.circular(8.0),
          border: Border.all(
            color: isActive ? NordicColors.primary : NordicColors.border,
          ),
        ),
        child: Text(
          title,
          textAlign: TextAlign.center,
          style: TextStyle(
            fontSize: 11.0,
            fontWeight: isActive || isDone ? FontWeight.bold : FontWeight.normal,
            color: isActive
                ? Colors.white
                : isDone
                    ? NordicColors.primary
                    : NordicColors.textSecondary,
          ),
        ),
      ),
    );
  }

  Widget _buildStepContent() {
    switch (_currentStep) {
      case 0:
        return NordicCard(
          child: Padding(
            padding: const EdgeInsets.all(16.0),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Text('Mặt Trước CCCD', style: NordicTypography.h3),
                const SizedBox(height: 8.0),
                const Text(
                  'Vui lòng chụp rõ các thông tin số CCCD, họ tên và ngày sinh.',
                  style: NordicTypography.bodySmall,
                ),
                const SizedBox(height: 16.0),
                _buildPhotoPreview(_frontCccdController.text, 'CCCD Mặt Trước'),
                const SizedBox(height: 16.0),
                NordicTextInput(
                  label: 'Đường dẫn ảnh CCCD Mặt Trước',
                  controller: _frontCccdController,
                ),
              ],
            ),
          ),
        );
      case 1:
        return NordicCard(
          child: Padding(
            padding: const EdgeInsets.all(16.0),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Text('Mặt Sau CCCD', style: NordicTypography.h3),
                const SizedBox(height: 8.0),
                const Text(
                  'Vui lòng chụp rõ thông tin đặc điểm nhận dạng và ngày cấp.',
                  style: NordicTypography.bodySmall,
                ),
                const SizedBox(height: 16.0),
                _buildPhotoPreview(_backCccdController.text, 'CCCD Mặt Sau'),
                const SizedBox(height: 16.0),
                NordicTextInput(
                  label: 'Đường dẫn ảnh CCCD Mặt Sau',
                  controller: _backCccdController,
                ),
              ],
            ),
          ),
        );
      case 2:
        return NordicCard(
          child: Padding(
            padding: const EdgeInsets.all(16.0),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Text('Ảnh Chân Dung (Selfie)', style: NordicTypography.h3),
                const SizedBox(height: 8.0),
                const Text(
                  'Chụp ảnh khuôn mặt chính diện, không đeo kính râm hay khẩu trang.',
                  style: NordicTypography.bodySmall,
                ),
                const SizedBox(height: 16.0),
                _buildPhotoPreview(_selfieController.text, 'Ảnh Chân Dung'),
                const SizedBox(height: 16.0),
                NordicTextInput(
                  label: 'Đường dẫn ảnh Chân dung Selfie',
                  controller: _selfieController,
                ),
              ],
            ),
          ),
        );
      case 3:
      default:
        return NordicCard(
          child: Padding(
            padding: const EdgeInsets.all(16.0),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Text('Xác Nhận Thông Tin Ảnh eKYC', style: NordicTypography.h3),
                const SizedBox(height: 12.0),
                _buildSummaryTile('1. CCCD Mặt Trước', _frontCccdController.text),
                const Divider(),
                _buildSummaryTile('2. CCCD Mặt Sau', _backCccdController.text),
                const Divider(),
                _buildSummaryTile('3. Selfie Chân Dung', _selfieController.text),
              ],
            ),
          ),
        );
    }
  }

  Widget _buildPhotoPreview(String url, String label) {
    return Container(
      height: 140.0,
      width: double.infinity,
      decoration: BoxDecoration(
        color: const Color(0xFFF1F5F9),
        borderRadius: BorderRadius.circular(12.0),
        border: Border.all(color: NordicColors.border),
      ),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          const Icon(Icons.badge_outlined, size: 40.0, color: NordicColors.primary),
          const SizedBox(height: 8.0),
          Text(label, style: NordicTypography.labelMedium),
          const SizedBox(height: 4.0),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16.0),
            child: Text(
              url,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(fontSize: 11.0, color: NordicColors.textSecondary),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildSummaryTile(String title, String url) {
    return Row(
      children: [
        const Icon(Icons.check_circle, color: Color(0xFF16A34A), size: 20.0),
        const SizedBox(width: 8.0),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(title, style: NordicTypography.labelMedium),
              Text(
                url,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(fontSize: 12.0, color: NordicColors.textSecondary),
              ),
            ],
          ),
        ),
      ],
    );
  }

  Widget _buildActionButtons() {
    if (_currentStep < 3) {
      return Row(
        children: [
          if (_currentStep > 0) ...[
            Expanded(
              child: NordicButton(
                label: 'Quay lại',
                variant: NordicButtonVariant.secondary,
                onPressed: () => setState(() => _currentStep--),
              ),
            ),
            const SizedBox(width: 12.0),
          ],
          Expanded(
            child: NordicButton(
              label: 'Tiếp theo',
              variant: NordicButtonVariant.primary,
              onPressed: () => setState(() => _currentStep++),
            ),
          ),
        ],
      );
    }

    return Column(
      children: [
        NordicButton(
          label: 'Gửi Xác Thực eKYC',
          variant: NordicButtonVariant.primary,
          isLoading: _isLoading,
          width: double.infinity,
          onPressed: _submitEkyc,
        ),
        const SizedBox(height: 8.0),
        NordicButton(
          label: 'Chỉnh sửa lại ảnh',
          variant: NordicButtonVariant.ghost,
          onPressed: () => setState(() => _currentStep = 0),
        ),
      ],
    );
  }

  Widget _buildResultView(EkycResultResponse result) {
    final bool isApproved = result.kycStatus == 'APPROVED';

    return Column(
      children: [
        EmptyStateWidget(
          title: isApproved ? 'Xác Thực Thành Công!' : 'Đang Chờ Duyệt Thủ Công',
          subtitle: isApproved
              ? 'Tài khoản của bạn đã đạt độ tin cậy eKYC (${result.confidenceScore.toStringAsFixed(1)}%). Bạn hiện ở trạng thái SẴN SÀNG (IDLE) để nhận ca làm.'
              : 'Độ tin cậy eKYC (${result.confidenceScore.toStringAsFixed(1)}%) cần được Admin xem xét thủ công.',
          icon: isApproved ? Icons.verified_outlined : Icons.hourglass_top_rounded,
        ),
        const SizedBox(height: 16.0),
        NordicCard(
          child: Padding(
            padding: const EdgeInsets.all(16.0),
            child: Column(
              children: [
                _buildResultRow('Mã Thợ (Worker ID)', result.workerId.toString()),
                const Divider(),
                _buildResultRow('Độ Tin Cậy eKYC', '${result.confidenceScore.toStringAsFixed(1)}%'),
                const Divider(),
                _buildResultRow('Trạng Thái eKYC', result.kycStatus),
                const Divider(),
                _buildResultRow('Phê Duyệt Tự Động', result.autoApproved ? 'Có (>= 85%)' : 'Không'),
              ],
            ),
          ),
        ),
        const SizedBox(height: 20.0),
        NordicButton(
          label: 'Thực Hiện Lại eKYC',
          variant: NordicButtonVariant.secondary,
          width: double.infinity,
          onPressed: () {
            setState(() {
              _result = null;
              _currentStep = 0;
            });
          },
        ),
      ],
    );
  }

  Widget _buildResultRow(String label, String value) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4.0),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Text(label, style: NordicTypography.bodyRegular),
          Text(value, style: NordicTypography.labelMedium),
        ],
      ),
    );
  }
}
