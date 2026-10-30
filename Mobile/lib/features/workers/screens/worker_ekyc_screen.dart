import 'package:flutter/material.dart';

import '../../../core/motion/error_shake.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/nordic_text_input.dart';
import '../../../core/widgets/status_pill.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../../../core/widgets/step_dots.dart';
import '../models/ekyc_models.dart';
import '../services/workers_ekyc_service.dart';

/// Screen for Worker eKYC Identity Verification (CCCD 2 mặt + selfie).
/// Implements MOB-M4-01 according to contract workers.md §2.2.
/// MOB-UI-24 (`Mobile/DESIGN-SYSTEM-WORKER.md`): `NordicPage.pushed`, the step buttons in `actions`, tokens only,
/// a failed step shakes its message, the result as words.
class WorkerEkycScreen extends StatefulWidget {
  final WorkersEkycService? ekycService;

  const WorkerEkycScreen({super.key, this.ekycService});

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

  /// Bumped on every failure so the message shakes.
  int _errors = 0;

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

  /// Closes the keyboard of a photo step so it never stays open on a screen without an input: the action bar sits
  /// above the keyboard, so an open keyboard would push the send button up the confirmation step.
  void _closeKeyboard() => FocusManager.instance.primaryFocus?.unfocus();

  void _goToStep(int step) {
    _closeKeyboard();
    setState(() => _currentStep = step);
  }

  Future<void> _submitEkyc() async {
    _closeKeyboard();
    final frontUrl = _frontCccdController.text.trim();
    final backUrl = _backCccdController.text.trim();
    final selfieUrl = _selfieController.text.trim();

    if (frontUrl.isEmpty || backUrl.isEmpty || selfieUrl.isEmpty) {
      setState(() {
        _errors++;
        _errorMessage =
            'Bạn chưa đủ 3 ảnh (CCCD mặt trước, mặt sau và ảnh chân dung).';
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
        _errors++;
        _errorMessage = e.toString();
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final result = _result;
    return NordicPage.pushed(
      title: 'Xác minh danh tính',
      actions: result == null
          ? [_buildActionButtons()]
          : [
              NordicButton(
                label: 'Làm lại xác minh',
                variant: NordicButtonVariant.secondary,
                onPressed: () {
                  _closeKeyboard();
                  setState(() {
                    _result = null;
                    _currentStep = 0;
                  });
                },
              ),
            ],
      children: [
        const Text('Chụp CCCD và ảnh chân dung', style: NordicTypography.h2),
        const SizedBox(height: NordicSpace.xxs),
        const Text(
          'Thợ freelance cần xác minh danh tính trước khi nhận ca làm.',
          style: NordicTypography.bodySmall,
        ),
        const SizedBox(height: NordicSpace.md),
        if (result != null)
          ..._buildResultView(result)
        else ...[
          _buildStepIndicator(),
          const SizedBox(height: NordicSpace.md),
          ErrorShake(
            errors: _errors,
            child: _errorMessage == null
                ? const SizedBox.shrink()
                : Padding(
                    padding: const EdgeInsets.only(bottom: NordicSpace.md),
                    child: Container(
                      width: double.infinity,
                      padding: const EdgeInsets.all(NordicSpace.sm),
                      decoration: const BoxDecoration(
                        color: NordicColors.errorContainer,
                        borderRadius: NordicRadius.tileAll,
                      ),
                      child: Text(
                        _errorMessage!,
                        style: NordicTypography.bodyRegular.copyWith(
                          color: NordicColors.error,
                        ),
                      ),
                    ),
                  ),
          ),
          _buildStepContent(),
        ],
      ],
    );
  }

  Widget _buildStepIndicator() {
    DotState state(int i) => _currentStep > i
        ? DotState.done
        : _currentStep == i
        ? DotState.current
        : DotState.todo;
    return StepDots(
      steps: [
        StepItem('Mặt trước', state(0)),
        StepItem('Mặt sau', state(1)),
        StepItem('Chân dung', state(2)),
        StepItem('Gửi', state(3)),
      ],
    );
  }

  Widget _photoStep({
    required String title,
    required String hint,
    required TextEditingController controller,
    required String previewLabel,
    required String inputLabel,
  }) {
    return NordicCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(title, style: NordicTypography.titleMedium),
          const SizedBox(height: NordicSpace.xs),
          Text(hint, style: NordicTypography.bodySmall),
          const SizedBox(height: NordicSpace.md),
          _buildPhotoPreview(controller.text, previewLabel),
          const SizedBox(height: NordicSpace.md),
          NordicTextInput(label: inputLabel, controller: controller),
        ],
      ),
    );
  }

  Widget _buildStepContent() {
    switch (_currentStep) {
      case 0:
        return _photoStep(
          title: 'Mặt trước CCCD',
          hint: 'Chụp rõ số CCCD, họ tên và ngày sinh.',
          controller: _frontCccdController,
          previewLabel: 'CCCD mặt trước',
          inputLabel: 'Đường dẫn ảnh CCCD mặt trước',
        );
      case 1:
        return _photoStep(
          title: 'Mặt sau CCCD',
          hint: 'Chụp rõ đặc điểm nhận dạng và ngày cấp.',
          controller: _backCccdController,
          previewLabel: 'CCCD mặt sau',
          inputLabel: 'Đường dẫn ảnh CCCD mặt sau',
        );
      case 2:
        return _photoStep(
          title: 'Ảnh chân dung',
          hint: 'Chụp chính diện khuôn mặt, không đeo kính râm hay khẩu trang.',
          controller: _selfieController,
          previewLabel: 'Ảnh khuôn mặt',
          inputLabel: 'Đường dẫn ảnh chân dung',
        );
      case 3:
      default:
        return NordicCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Text(
                'Xác nhận ảnh sẽ gửi',
                style: NordicTypography.titleMedium,
              ),
              const SizedBox(height: NordicSpace.sm),
              _buildSummaryTile('1. CCCD mặt trước', _frontCccdController.text),
              const Divider(),
              _buildSummaryTile('2. CCCD mặt sau', _backCccdController.text),
              const Divider(),
              _buildSummaryTile('3. Ảnh chân dung', _selfieController.text),
            ],
          ),
        );
    }
  }

  /// A framed picture slot with its label and the address of the photo.
  Widget _buildPhotoPreview(String url, String label) {
    return Container(
      height: 92.0,
      width: double.infinity,
      padding: const EdgeInsets.symmetric(horizontal: NordicSpace.md),
      decoration: BoxDecoration(
        color: NordicColors.surface,
        borderRadius: NordicRadius.cardAll,
        border: Border.all(color: NordicColors.border),
      ),
      child: Row(
        children: [
          Container(
            width: 84.0,
            height: 56.0,
            decoration: const BoxDecoration(
              color: NordicColors.surfaceContainer,
              borderRadius: NordicRadius.tileAll,
            ),
            child: const Icon(
              Icons.badge_outlined,
              size: 28.0,
              color: NordicColors.primary,
            ),
          ),
          const SizedBox(width: NordicSpace.sm),
          Expanded(
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(label, style: NordicTypography.titleSmall),
                const SizedBox(height: NordicSpace.xxs),
                Text(
                  url,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: NordicTypography.bodySmall,
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildSummaryTile(String title, String url) {
    return Row(
      children: [
        const Icon(Icons.check_circle, color: NordicColors.primary, size: 20.0),
        const SizedBox(width: NordicSpace.xs),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(title, style: NordicTypography.titleSmall),
              Text(
                url,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: NordicTypography.bodySmall.copyWith(
                  color: NordicColors.textSecondary,
                ),
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
                onPressed: () => _goToStep(_currentStep - 1),
              ),
            ),
            const SizedBox(width: NordicSpace.sm),
          ],
          Expanded(
            child: NordicButton(
              label: 'Tiếp theo',
              variant: NordicButtonVariant.primary,
              onPressed: () => _goToStep(_currentStep + 1),
            ),
          ),
        ],
      );
    }

    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        NordicButton(
          label: 'Gửi xác minh',
          variant: NordicButtonVariant.primary,
          isLoading: _isLoading,
          width: double.infinity,
          onPressed: _submitEkyc,
        ),
        const SizedBox(height: NordicSpace.xs),
        NordicButton(
          label: 'Chỉnh sửa ảnh',
          variant: NordicButtonVariant.ghost,
          onPressed: () => _goToStep(0),
        ),
      ],
    );
  }

  List<Widget> _buildResultView(EkycResultResponse result) {
    final bool isApproved = result.kycStatus == 'APPROVED';

    return [
      EmptyStateWidget(
        title: isApproved ? 'Xác minh thành công!' : 'Đang chờ duyệt',
        subtitle: isApproved
            ? 'Tài khoản của bạn đã đạt độ tin cậy ${result.confidenceScore.toStringAsFixed(1)}%. Bạn đã sẵn sàng nhận ca làm.'
            : 'Độ tin cậy ${result.confidenceScore.toStringAsFixed(1)}% cần quản trị viên xem xét thủ công.',
        icon: isApproved
            ? Icons.verified_outlined
            : Icons.hourglass_top_rounded,
      ),
      const SizedBox(height: NordicSpace.md),
      NordicCard(
        child: Column(
          children: [
            _buildResultRow('Mã thợ', result.workerId.toString()),
            const Divider(),
            _buildResultRow(
              'Độ tin cậy',
              '${result.confidenceScore.toStringAsFixed(1)}%',
            ),
            const Divider(),
            Padding(
              padding: const EdgeInsets.symmetric(vertical: NordicSpace.xxs),
              child: Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  const Text('Trạng thái', style: NordicTypography.bodyRegular),
                  StatusPill(
                    isApproved ? 'Đã duyệt' : 'Chờ duyệt',
                    tone: isApproved ? PillTone.ok : PillTone.warn,
                  ),
                ],
              ),
            ),
            const Divider(),
            _buildResultRow(
              'Duyệt tự động',
              result.autoApproved ? 'Có (từ 85%)' : 'Không',
            ),
          ],
        ),
      ),
    ];
  }

  Widget _buildResultRow(String label, String value) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: NordicSpace.xxs),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Expanded(child: Text(label, style: NordicTypography.bodyRegular)),
          const SizedBox(width: NordicSpace.xs),
          Flexible(
            child: Text(
              value,
              textAlign: TextAlign.end,
              style: NordicTypography.titleSmall,
            ),
          ),
        ],
      ),
    );
  }
}
