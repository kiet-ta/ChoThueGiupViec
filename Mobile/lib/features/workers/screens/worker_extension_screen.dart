import 'package:flutter/material.dart';

import '../../../core/motion/error_shake.dart';
import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/avatar_initials.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/status_pill.dart';
import '../../../core/widgets/step_dots.dart';
import '../models/worker_extension_models.dart';
import '../services/worker_extension_service.dart';

/// Screen for Worker responding to extension request ("Làm lần 2", BR-08, contract workers.md §2.6).
/// Implements MOB-M4-06.
/// MOB-UI-22 (`Mobile/DESIGN-SYSTEM-WORKER.md`): `NordicPage.pushed`, both answers in `actions`,
/// tokens only, a failed answer shakes its message.
class WorkerExtensionScreen extends StatefulWidget {
  /// The extension to answer; there is no default one (a made-up offer could be answered as if it were real).
  final WorkerExtensionOffer offer;
  final WorkerExtensionService? service;
  final void Function(WorkerExtensionResponseDto response)? onResponded;

  const WorkerExtensionScreen({
    super.key,
    required this.offer,
    this.service,
    this.onResponded,
  });

  @override
  State<WorkerExtensionScreen> createState() => _WorkerExtensionScreenState();
}

class _WorkerExtensionScreenState extends State<WorkerExtensionScreen> {
  late final WorkerExtensionService _service;
  late final WorkerExtensionOffer _offer;

  bool _isSubmitting = false;
  String? _errorMessage;
  WorkerExtensionResponseDto? _responseResult;

  /// Bumped on every failed answer so the message shakes.
  int _errors = 0;

  @override
  void initState() {
    super.initState();
    _service = widget.service ?? WorkerExtensionService();

    _offer = widget.offer;
  }

  Future<void> _handleResponse({required bool accept}) async {
    setState(() {
      _isSubmitting = true;
      _errorMessage = null;
    });

    try {
      final res = await _service.respondExtension(
        assignmentId: _offer.assignmentId,
        accept: accept,
      );

      if (!mounted) return;

      final answer = res.data;
      if (!res.success || answer == null) {
        _fail('Máy chủ không trả kết quả của yêu cầu. Vui lòng thử lại.');
        return;
      }
      setState(() {
        _isSubmitting = false;
        _responseResult = answer;
      });
      widget.onResponded?.call(answer);
    } on ApiException catch (e) {
      _fail(_failure(e));
    } catch (_) {
      _fail('Không gửi được câu trả lời. Vui lòng thử lại.');
    }
  }

  /// The words of a failed answer: a fixed text by status, the server's own for a network failure; never a made-up success.
  String _failure(ApiException e) {
    if (e.isNetworkError) return e.message;
    if (e.isNotFound || e.isForbidden) {
      return 'Không tìm thấy yêu cầu gia hạn của ca này.';
    }
    if (e.isConflict) {
      return 'Yêu cầu này đã được trả lời hoặc chưa được thanh toán.';
    }
    return e.message.isNotEmpty
        ? e.message
        : 'Không gửi được câu trả lời. Vui lòng thử lại.';
  }

  void _fail(String message) {
    if (!mounted) return;
    setState(() {
      _isSubmitting = false;
      _errors++;
      _errorMessage = message;
    });
  }

  /// `2` for whole hours, `1,5` for half hours.
  String _hoursText(double hours) {
    final whole = hours == hours.roundToDouble();
    return whole
        ? hours.round().toString()
        : hours.toString().replaceAll('.', ',');
  }

  String _formatVnd(int amount) {
    final str = amount.toString();
    final buffer = StringBuffer();
    for (int i = 0; i < str.length; i++) {
      if (i > 0 && (str.length - i) % 3 == 0) {
        buffer.write('.');
      }
      buffer.write(str[i]);
    }
    return '${buffer.toString()} đ';
  }

  @override
  Widget build(BuildContext context) {
    final result = _responseResult;
    return NordicPage.pushed(
      title: 'Làm thêm giờ',
      actions: _buildActions(),
      children: result != null ? _buildResultView(result) : _buildOfferView(),
    );
  }

  List<Widget> _buildOfferView() {
    return [
      // The one big number: the hours the customer asks for.
      Column(
        children: [
          const Text('Gia chủ muốn thêm', style: NordicTypography.bodySmall),
          const SizedBox(height: NordicSpace.xs),
          Text(
            '${_hoursText(_offer.extraHours)} giờ',
            style: NordicTypography.numberLarge,
          ),
        ],
      ),
      const SizedBox(height: NordicSpace.gutter),

      ErrorShake(
        errors: _errors,
        child: _errorMessage == null
            ? const SizedBox.shrink()
            : Padding(
                padding: const EdgeInsets.only(bottom: NordicSpace.md),
                child: Container(
                  padding: const EdgeInsets.all(NordicSpace.sm),
                  decoration: const BoxDecoration(
                    color: NordicColors.errorContainer,
                    borderRadius: NordicRadius.tileAll,
                  ),
                  child: Row(
                    children: [
                      const Icon(
                        Icons.error_outline,
                        color: NordicColors.error,
                      ),
                      const SizedBox(width: NordicSpace.xs),
                      Expanded(
                        child: Text(
                          _errorMessage!,
                          style: NordicTypography.bodySmall.copyWith(
                            color: NordicColors.error,
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
      ),

      // Customer and place
      NordicCard(
        child: Row(
          children: [
            AvatarInitials(_offer.customerName, size: 44),
            const SizedBox(width: NordicSpace.sm),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(_offer.customerName, style: NordicTypography.titleSmall),
                  const SizedBox(height: NordicSpace.xxs),
                  Text(_offer.workZone, style: NordicTypography.bodySmall),
                ],
              ),
            ),
          ],
        ),
      ),
      const SizedBox(height: NordicSpace.sm),

      // New end time and the extra pay
      NordicCard(
        child: Column(
          children: [
            _buildDetailRow(
              'Giờ kết thúc mới:',
              '${_offer.currentEndTime} ➔ ${_offer.newEndTime}',
            ),
            const SizedBox(height: NordicSpace.xs),
            _buildDetailRow(
              'Thù lao làm thêm:',
              '+${_formatVnd(_offer.extraAmount)}',
              isHighlight: true,
            ),
          ],
        ),
      ),
      const SizedBox(height: NordicSpace.sm),

      // Where the answer stands (owner decision C: the worker answers before any payment)
      const NordicCard(
        child: StepDots(
          steps: [
            StepItem('Gia chủ gửi', DotState.done),
            StepItem('Bạn đồng ý', DotState.current),
            StepItem('Gia chủ trả', DotState.todo),
          ],
        ),
      ),
      const SizedBox(height: NordicSpace.sm),
      const Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(
            Icons.info_outline,
            size: 16.0,
            color: NordicColors.textSecondary,
          ),
          SizedBox(width: NordicSpace.xs),
          Expanded(
            child: Text(
              'Gia chủ đã thanh toán trước khoản thù lao này. Nếu bạn đồng ý, ca làm sẽ được nối dài ngay.',
              style: NordicTypography.bodySmall,
            ),
          ),
        ],
      ),
    ];
  }

  /// Decline and accept, pinned at the bottom while the answer has not been sent; "Quay lại ca làm" after it.
  List<Widget>? _buildActions() {
    if (_responseResult != null) {
      return [
        NordicButton(
          label: 'Quay lại ca làm',
          variant: NordicButtonVariant.primary,
          onPressed: () {
            Navigator.of(context).maybePop();
          },
        ),
      ];
    }
    if (_isSubmitting) {
      return const [Center(child: CircularProgressIndicator())];
    }
    return [
      Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          NordicButton(
            label: 'Đồng ý nối ca (+${_formatVnd(_offer.extraAmount)})',
            variant: NordicButtonVariant.primary,
            onPressed: () => _handleResponse(accept: true),
          ),
          const SizedBox(height: NordicSpace.xs),
          NordicButton(
            label: 'Từ chối làm thêm',
            variant: NordicButtonVariant.secondary,
            onPressed: () => _handleResponse(accept: false),
          ),
        ],
      ),
    ];
  }

  Widget _buildDetailRow(
    String label,
    String value, {
    bool isHighlight = false,
  }) {
    return Row(
      mainAxisAlignment: MainAxisAlignment.spaceBetween,
      children: [
        Expanded(
          child: Text(
            label,
            style: NordicTypography.bodyRegular.copyWith(
              color: NordicColors.textSecondary,
            ),
          ),
        ),
        const SizedBox(width: NordicSpace.xs),
        Flexible(
          child: Text(
            value,
            textAlign: TextAlign.end,
            style: isHighlight
                ? NordicTypography.titleMedium.copyWith(
                    color: NordicColors.primary,
                  )
                : NordicTypography.titleSmall,
          ),
        ),
      ],
    );
  }

  List<Widget> _buildResultView(WorkerExtensionResponseDto result) {
    final isAccepted = result.status == 'ACCEPTED';

    return [
      const SizedBox(height: NordicSpace.gutter),
      Icon(
        isAccepted ? Icons.check_circle : Icons.cancel,
        size: 64.0,
        color: isAccepted ? NordicColors.primary : NordicColors.error,
        semanticLabel: isAccepted ? 'Đã đồng ý' : 'Đã từ chối',
      ),
      const SizedBox(height: NordicSpace.md),
      Text(
        isAccepted ? 'Đã đồng ý nối ca' : 'Đã từ chối yêu cầu',
        textAlign: TextAlign.center,
        style: NordicTypography.h2,
      ),
      const SizedBox(height: NordicSpace.xs),
      Text(
        isAccepted
            ? 'Ca làm của bạn đã được gia hạn thêm ${_hoursText(result.extraHours)}h đến ${result.newEndTime}.'
            : 'Bạn đã từ chối yêu cầu làm thêm giờ. Gia chủ sẽ được hoàn 100% chi phí gia hạn.',
        textAlign: TextAlign.center,
        style: NordicTypography.bodyRegular.copyWith(
          color: NordicColors.textSecondary,
        ),
      ),
      const SizedBox(height: NordicSpace.lg),

      NordicCard(
        child: Column(
          children: [
            _buildDetailRow('Mã ca làm:', '#${result.assignmentId}'),
            const SizedBox(height: NordicSpace.xs),
            _buildDetailRow('Mã gia hạn:', '#${result.extensionId}'),
            const SizedBox(height: NordicSpace.xs),
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text(
                  'Trạng thái:',
                  style: NordicTypography.bodyRegular.copyWith(
                    color: NordicColors.textSecondary,
                  ),
                ),
                StatusPill(
                  isAccepted ? 'Đã đồng ý' : 'Đã từ chối',
                  tone: isAccepted ? PillTone.ok : PillTone.bad,
                ),
              ],
            ),
            if (isAccepted) ...[
              const SizedBox(height: NordicSpace.xs),
              _buildDetailRow('Giờ kết thúc mới:', result.newEndTime),
              const SizedBox(height: NordicSpace.xs),
              _buildDetailRow(
                'Thù lao cộng thêm:',
                '+${_formatVnd(result.extraAmount)}',
                isHighlight: true,
              ),
            ],
          ],
        ),
      ),
    ];
  }
}
