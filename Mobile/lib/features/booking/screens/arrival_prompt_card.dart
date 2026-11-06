import 'package:flutter/material.dart';

import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../services/order_service.dart';

/// Sentence for the customer arrival confirmation card (CUST-ARRIVAL-01).
String arrivalDescription(String? workerName, double? distanceMeters) {
  final who = (workerName != null && workerName.trim().isNotEmpty)
      ? 'Thợ ${workerName.trim()}'
      : 'Thợ';
  final far = (distanceMeters != null && distanceMeters.isFinite)
      ? ' (cách khoảng ${distanceMeters.round()} m)'
      : '';
  return '$who báo đã có mặt nhưng GPS chưa khớp địa chỉ$far. Nếu thợ đã có mặt tại nhà, bạn hãy xác nhận để thợ bắt đầu ca làm.';
}

/// The card shown to the customer when a worker reports arrival with GPS deviation (>100 m)
/// and waits for customer confirmation (BR-04, CUST-ARRIVAL-01).
class ArrivalPromptCard extends StatefulWidget {
  final int assignmentId;
  final String? workerName;
  final double? distanceMeters;
  final IOrderService service;
  final VoidCallback? onConfirmed;

  const ArrivalPromptCard({
    super.key,
    required this.assignmentId,
    this.workerName,
    this.distanceMeters,
    required this.service,
    this.onConfirmed,
  });

  @override
  State<ArrivalPromptCard> createState() => _ArrivalPromptCardState();
}

class _ArrivalPromptCardState extends State<ArrivalPromptCard> {
  bool _asking = false;
  bool _submitting = false;
  String? _error;
  bool _confirmed = false;

  Future<void> _confirm() async {
    if (_submitting) return;
    setState(() {
      _submitting = true;
      _error = null;
    });
    try {
      await widget.service.confirmArrival(widget.assignmentId);
      if (!mounted) return;
      setState(() {
        _submitting = false;
        _confirmed = true;
      });
      widget.onConfirmed?.call();
    } on ApiException catch (e) {
      if (!mounted) return;
      final msg = switch (e.statusCode) {
        409 => 'Thợ chưa báo đến nơi hoặc ca làm đã được xác nhận.',
        404 => 'Không tìm thấy ca làm này.',
        _ =>
          e.message.isNotEmpty
              ? e.message
              : 'Không xác nhận được. Vui lòng thử lại.',
      };
      setState(() {
        _submitting = false;
        _error = msg;
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _submitting = false;
        _error = 'Không xác nhận được. Vui lòng thử lại.';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_confirmed) {
      return const NordicCard(
        child: Row(
          children: [
            Icon(
              Icons.check_circle_outline,
              color: NordicColors.primary,
              size: 20,
            ),
            SizedBox(width: NordicSpace.xs),
            Expanded(
              child: Text(
                'Đã xác nhận thợ có mặt tại nhà.',
                style: NordicTypography.bodyRegular,
              ),
            ),
          ],
        ),
      );
    }

    final who =
        (widget.workerName != null && widget.workerName!.trim().isNotEmpty)
        ? 'Thợ ${widget.workerName!.trim()}'
        : 'Thợ';

    return NordicCard(
      isHighlighted: true,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              const Icon(
                Icons.location_on_outlined,
                color: NordicColors.primary,
                size: 22,
              ),
              const SizedBox(width: NordicSpace.xs),
              Expanded(
                child: Text(
                  'Thợ báo đã đến nơi',
                  key: Key('arrival-title-${widget.assignmentId}'),
                  style: NordicTypography.h3,
                ),
              ),
            ],
          ),
          const SizedBox(height: NordicSpace.xs),
          Text(
            arrivalDescription(widget.workerName, widget.distanceMeters),
            key: Key('arrival-desc-${widget.assignmentId}'),
            style: NordicTypography.bodyRegular,
          ),
          if (_error != null) ...[
            const SizedBox(height: NordicSpace.xs),
            Text(
              _error!,
              key: Key('arrival-error-${widget.assignmentId}'),
              style: NordicTypography.bodySmall.copyWith(
                color: NordicColors.error,
              ),
            ),
          ],
          const SizedBox(height: NordicSpace.sm),
          if (!_asking)
            NordicButton(
              key: Key('arrival-btn-start-${widget.assignmentId}'),
              label: 'Xác nhận thợ đã đến',
              onPressed: () => setState(() => _asking = true),
            )
          else ...[
            Text(
              'Bạn xác nhận $who đang có mặt tại nhà?',
              style: NordicTypography.labelMedium,
            ),
            const SizedBox(height: NordicSpace.xs),
            Row(
              children: [
                Expanded(
                  child: NordicButton(
                    key: Key('arrival-btn-confirm-${widget.assignmentId}'),
                    label: 'Đúng, thợ ở đây',
                    isLoading: _submitting,
                    onPressed: _confirm,
                  ),
                ),
                const SizedBox(width: NordicSpace.xs),
                Expanded(
                  child: NordicButton(
                    key: Key('arrival-btn-cancel-${widget.assignmentId}'),
                    label: 'Chưa',
                    variant: NordicButtonVariant.secondary,
                    onPressed: _submitting
                        ? null
                        : () => setState(() => _asking = false),
                  ),
                ),
              ],
            ),
          ],
        ],
      ),
    );
  }
}
