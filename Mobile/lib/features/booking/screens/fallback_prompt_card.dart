import 'package:flutter/material.dart';

import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../logic/booking_logic.dart';
import '../models/fallback_models.dart';
import '../services/fallback_service.dart';

/// The BR-02 fallback question of an order that is waiting in `AWAITING_FALLBACK` (dispatch.md 3.2):
/// "Chỉ có 1 thợ: thợ làm 2 ca liên tiếp?". Agree: the worker must confirm the second shift. Decline: the order is cancelled and
/// refunded in full. Silence until the shift starts counts as a decline (the server enforces it).
class FallbackPromptCard extends StatefulWidget {
  final int orderId;
  final IFallbackService service;

  /// Called after the customer answered, so the order screen reloads the order.
  final VoidCallback? onAnswered;

  const FallbackPromptCard({
    super.key,
    required this.orderId,
    required this.service,
    this.onAnswered,
  });

  @override
  State<FallbackPromptCard> createState() => _FallbackPromptCardState();
}

class _FallbackPromptCardState extends State<FallbackPromptCard> {
  FallbackStatus? _status;
  String? _error;
  String? _notice;
  bool _loading = true;
  bool _submitting = false; // one request per tap

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final status = await widget.service.getStatus(widget.orderId);
      if (!mounted) return;
      setState(() {
        _status = status;
        _loading = false;
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _error = e.message;
        _loading = false;
      });
    }
  }

  Future<void> _answer(bool accept) async {
    if (_submitting) return;
    setState(() {
      _submitting = true;
      _error = null;
    });
    try {
      final response = await widget.service.respond(
        widget.orderId,
        accept: accept,
      );
      if (!mounted) return;
      final asked = response.outcome == FallbackOutcome.workerAsked;
      setState(() {
        _submitting = false;
        _notice = asked
            ? 'Đã gửi đề nghị. Thợ có 2 phút để xác nhận ca thứ hai.'
            : 'Đơn đã được huỷ và hoàn tiền 100 %.';
        _status = FallbackStatus(
          orderId: widget.orderId,
          state: asked
              ? FallbackState.awaitingWorker
              : FallbackState.notWaiting,
        );
      });
      widget.onAnswered?.call();
    } on ApiException catch (e) {
      if (!mounted) return;
      final stale =
          e.code == FallbackErrorCode.notWaiting ||
          e.code == FallbackErrorCode.alreadyAnswered;
      setState(() {
        _submitting = false;
        _error = stale
            ? 'Lựa chọn này không còn hiệu lực. Đang tải lại đơn.'
            : e.message;
      });
      if (stale) {
        widget.onAnswered?.call();
        _load();
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) {
      return const NordicCard(
        child: Padding(
          padding: EdgeInsets.all(16.0),
          child: Center(child: CircularProgressIndicator()),
        ),
      );
    }
    final status = _status;
    if (status == null) {
      return _card(
        children: [
          Text(
            _error ?? 'Không tải được lựa chọn.',
            key: const Key('fallback-error'),
            style: NordicTypography.bodySmall.copyWith(
              color: NordicColors.error,
            ),
          ),
          const SizedBox(height: 12.0),
          NordicButton(
            key: const Key('fallback-retry'),
            label: 'Thử lại',
            variant: NordicButtonVariant.secondary,
            onPressed: _load,
          ),
        ],
      );
    }
    return switch (status.state) {
      FallbackState.awaitingCustomer => _question(status),
      FallbackState.awaitingWorker => _card(
        children: [
          const Text(
            'Đang chờ thợ xác nhận ca thứ hai',
            key: Key('fallback-waiting-worker'),
            style: NordicTypography.h3,
          ),
          const SizedBox(height: 8.0),
          Text(
            _notice ?? 'Bạn đã đồng ý. Thợ có 2 phút để xác nhận; nếu không, đơn sẽ được huỷ và hoàn tiền.',
            style: NordicTypography.bodyRegular,
          ),
        ],
      ),
      _ =>
        _notice == null
            ? const SizedBox.shrink()
            : _card(
                children: [
                  Text(
                    _notice!,
                    key: const Key('fallback-notice'),
                    style: NordicTypography.bodyRegular,
                  ),
                ],
              ),
    };
  }

  Widget _question(FallbackStatus status) => _card(
    children: [
      const Text(
        'Chỉ có 1 thợ nhận đơn của bạn',
        key: Key('fallback-title'),
        style: NordicTypography.h3,
      ),
      const SizedBox(height: 8.0),
      Text(
        'Thợ này làm luôn ${_secondShift(status)} ngay sau ca đã nhận (2 ca liên tiếp cùng ngày)? Nếu không đồng ý, đơn sẽ được huỷ và hoàn tiền 100 %.',
        key: const Key('fallback-question'),
        style: NordicTypography.bodyRegular,
      ),
      if (status.deadline != null) ...[
        const SizedBox(height: 4.0),
        Text(
          'Trả lời trước ${_hhmm(status.deadline!)}; nếu không, đơn sẽ được huỷ.',
          key: const Key('fallback-deadline'),
          style: NordicTypography.bodySmall,
        ),
      ],
      if (_error != null) ...[
        const SizedBox(height: 8.0),
        Text(
          _error!,
          key: const Key('fallback-error'),
          style: NordicTypography.bodySmall.copyWith(color: NordicColors.error),
        ),
      ],
      const SizedBox(height: 16.0),
      NordicButton(
        key: const Key('fallback-accept'),
        label: 'Đồng ý, thợ làm 2 ca liên tiếp',
        isLoading: _submitting,
        onPressed: _submitting ? null : () => _answer(true),
      ),
      const SizedBox(height: 8.0),
      NordicButton(
        key: const Key('fallback-decline'),
        label: 'Không, huỷ đơn và hoàn tiền',
        variant: NordicButtonVariant.secondary,
        onPressed: _submitting ? null : () => _answer(false),
      ),
    ],
  );

  /// `ca chiều`, `ca tối`; the server names the second shift, so the sentence never claims one it does not know.
  static String _secondShift(FallbackStatus status) {
    final code = status.secondShiftCode;
    return code == null ? 'ca liền sau' : shiftLabel(code).toLowerCase();
  }

  /// `08:00` in the phone's local time.
  static String _hhmm(DateTime utc) {
    final local = utc.toLocal();
    return '${local.hour.toString().padLeft(2, '0')}:${local.minute.toString().padLeft(2, '0')}';
  }

  Widget _card({required List<Widget> children}) => NordicCard(
    key: const Key('fallback-card'),
    child: Padding(
      padding: const EdgeInsets.all(16.0),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: children,
      ),
    ),
  );
}
