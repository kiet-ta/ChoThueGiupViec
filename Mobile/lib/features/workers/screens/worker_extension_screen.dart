import 'package:flutter/material.dart';

import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../models/worker_extension_models.dart';
import '../services/worker_extension_service.dart';
import '../widgets/tinted_card.dart';

/// Screen for Worker responding to extension request ("Làm lần 2", BR-08, contract workers.md §2.6).
/// Implements MOB-M4-06.
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
    return Scaffold(
      appBar: AppBar(title: Text('Làm Lần 2 #${_offer.assignmentId}')),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: _responseResult != null
              ? _buildResultView(_responseResult!)
              : _buildOfferView(),
        ),
      ),
    );
  }

  Widget _buildOfferView() {
    return ListView(
      padding: const EdgeInsets.all(16.0),
      children: [
        Text('Khách Hàng Yêu Cầu Làm Thêm Giờ', style: NordicTypography.h2),
        const SizedBox(height: 16.0),

        if (_errorMessage != null) ...[
          Container(
            padding: const EdgeInsets.all(12.0),
            decoration: BoxDecoration(
              color: NordicColors.errorContainer,
              borderRadius: BorderRadius.circular(8.0),
              border: Border.all(color: NordicColors.error),
            ),
            child: Row(
              children: [
                const Icon(Icons.error_outline, color: Color(0xFFDC2626)),
                const SizedBox(width: 8.0),
                Expanded(
                  child: Text(
                    _errorMessage!,
                    style: const TextStyle(
                      fontSize: 13.0,
                      color: Color(0xFF991B1B),
                    ),
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: 16.0),
        ],

        // Job & Customer Summary Card
        NordicCard(
          child: Padding(
            padding: const EdgeInsets.all(16.0),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    const CircleAvatar(
                      backgroundColor: NordicColors.surfaceSubtle,
                      child: Icon(Icons.person, color: NordicColors.primary),
                    ),
                    const SizedBox(width: 12.0),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(_offer.customerName, style: NordicTypography.h3),
                          const SizedBox(height: 2.0),
                          Text(
                            _offer.workZone,
                            style: const TextStyle(
                              fontSize: 12.0,
                              color: NordicColors.textSecondary,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ),
        const SizedBox(height: 16.0),

        // Extension Request Details Card
        TintedCard(
          backgroundColor: NordicColors.primaryFixed,
          child: Padding(
            padding: const EdgeInsets.all(16.0),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Row(
                  children: [
                    Icon(Icons.more_time, color: Color(0xFF16A34A)),
                    SizedBox(width: 8.0),
                    Flexible(
                      child: Text(
                        'Chi Tiết Tăng Giờ Ca Làm',
                        style: NordicTypography.labelMedium,
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 16.0),
                _buildDetailRow(
                  'Số giờ làm thêm:',
                  '${_hoursText(_offer.extraHours)} giờ',
                ),
                const SizedBox(height: 8.0),
                _buildDetailRow(
                  'Thời gian kết thúc mới:',
                  '${_offer.currentEndTime} ➔ ${_offer.newEndTime}',
                ),
                const SizedBox(height: 8.0),
                _buildDetailRow(
                  'Thù lao làm thêm:',
                  '+${_formatVnd(_offer.extraAmount)}',
                  isHighlight: true,
                ),
                const SizedBox(height: 12.0),
                const Divider(),
                const SizedBox(height: 8.0),
                const Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Icon(
                      Icons.info_outline,
                      size: 16.0,
                      color: NordicColors.textSecondary,
                    ),
                    SizedBox(width: 6.0),
                    Expanded(
                      child: Text(
                        'Khách hàng đã thanh toán trước khoản thù lao này. Nếu đồng ý, ca làm của bạn sẽ được nối dài ngay.',
                        style: TextStyle(
                          fontSize: 12.0,
                          color: NordicColors.textSecondary,
                        ),
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ),
        const SizedBox(height: 24.0),

        // Action Buttons
        if (_isSubmitting)
          const Center(
            child: Padding(
              padding: EdgeInsets.all(16.0),
              child: CircularProgressIndicator(),
            ),
          )
        else ...[
          NordicButton(
            label: 'Đồng Ý Nối Ca (+${_formatVnd(_offer.extraAmount)})',
            variant: NordicButtonVariant.primary,
            icon: const Icon(Icons.check_circle, size: 18.0),
            width: double.infinity,
            onPressed: () => _handleResponse(accept: true),
          ),
          const SizedBox(height: 12.0),
          NordicButton(
            label: 'Từ Chối Làm Thêm',
            variant: NordicButtonVariant.secondary,
            icon: const Icon(Icons.cancel_outlined, size: 18.0),
            width: double.infinity,
            onPressed: () => _handleResponse(accept: false),
          ),
        ],
      ],
    );
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
            style: const TextStyle(
              fontSize: 14.0,
              color: NordicColors.textSecondary,
            ),
          ),
        ),
        const SizedBox(width: 8.0),
        Flexible(
          child: Text(
            value,
            textAlign: TextAlign.end,
            style: TextStyle(
              fontSize: isHighlight ? 16.0 : 14.0,
              fontWeight: isHighlight ? FontWeight.bold : FontWeight.w600,
              color: isHighlight
                  ? NordicColors.primary
                  : NordicColors.textTitle,
            ),
          ),
        ),
      ],
    );
  }

  Widget _buildResultView(WorkerExtensionResponseDto result) {
    final isAccepted = result.status == 'ACCEPTED';

    return ListView(
      padding: const EdgeInsets.all(16.0),
      children: [
        const SizedBox(height: 20.0),
        Icon(
          isAccepted ? Icons.check_circle : Icons.cancel,
          size: 64.0,
          color: isAccepted ? NordicColors.primary : NordicColors.error,
        ),
        const SizedBox(height: 16.0),
        Text(
          isAccepted ? 'Đã Đồng Ý Nối Ca!' : 'Đã Từ Chối Yêu Cầu',
          textAlign: TextAlign.center,
          style: NordicTypography.h2,
        ),
        const SizedBox(height: 8.0),
        Text(
          isAccepted
              ? 'Ca làm của bạn đã được gia hạn thêm ${_hoursText(result.extraHours)}h đến ${result.newEndTime}.'
              : 'Bạn đã từ chối yêu cầu làm thêm giờ. Khách hàng sẽ được hoàn 100% chi phí gia hạn.',
          textAlign: TextAlign.center,
          style: const TextStyle(
            fontSize: 14.0,
            color: NordicColors.textSecondary,
          ),
        ),
        const SizedBox(height: 24.0),

        NordicCard(
          child: Padding(
            padding: const EdgeInsets.all(16.0),
            child: Column(
              children: [
                _buildDetailRow('Mã ca làm:', '#${result.assignmentId}'),
                const SizedBox(height: 8.0),
                _buildDetailRow('Mã gia hạn:', '#${result.extensionId}'),
                const SizedBox(height: 8.0),
                _buildDetailRow('Trạng thái:', result.status),
                if (isAccepted) ...[
                  const SizedBox(height: 8.0),
                  _buildDetailRow('Giờ kết thúc mới:', result.newEndTime),
                  const SizedBox(height: 8.0),
                  _buildDetailRow(
                    'Thù lao cộng thêm:',
                    '+${_formatVnd(result.extraAmount)}',
                    isHighlight: true,
                  ),
                ],
              ],
            ),
          ),
        ),
        const SizedBox(height: 24.0),

        NordicButton(
          label: 'Quay Lại Màn Thi Công',
          variant: NordicButtonVariant.primary,
          width: double.infinity,
          onPressed: () {
            Navigator.of(context).maybePop();
          },
        ),
      ],
    );
  }
}
