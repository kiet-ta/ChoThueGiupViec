import 'package:flutter/material.dart';

import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/status_pill.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../../../core/widgets/step_dots.dart';
import '../../payouts/logic/payout_logic.dart';
import '../logic/dispute_logic.dart';
import '../models/dispute_models.dart';
import '../services/disputes_service.dart';
import '../services/api_evidence_uploader.dart';
import '../services/evidence_uploader.dart';
import 'dispute_create_screen.dart';

/// Arguments of the dispute routes: who is signed in and, when a job is open, which order a new dispute is about.
class DisputeScreenArgs {
  final DisputeRole role;
  final int? orderId;

  const DisputeScreenArgs({required this.role, this.orderId});
}

/// The caller's own disputes with their status and verdict (MOB-M6-03); contract disputes.md 2.1.
class DisputeScreen extends StatefulWidget {
  final DisputeRole role;

  /// When set, a button starts a new dispute about this order.
  final int? orderId;
  final IDisputesService? service;
  final IEvidenceUploader? uploader;

  const DisputeScreen({
    super.key,
    required this.role,
    this.orderId,
    this.service,
    this.uploader,
  });

  @override
  State<DisputeScreen> createState() => _DisputeScreenState();
}

class _DisputeScreenState extends State<DisputeScreen> {
  late final IDisputesService _service;
  late final IEvidenceUploader _uploader;

  List<Dispute> _items = const [];
  String? _error;
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _service = widget.service ?? DisputesService();
    _uploader = widget.uploader ?? ApiEvidenceUploader(role: widget.role);
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final items = await _service.listMine(widget.role);
      if (!mounted) return;
      setState(() {
        _items = items;
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

  Future<void> _startNew() async {
    final created = await Navigator.of(context).push<bool>(
      MaterialPageRoute<bool>(
        builder: (_) => DisputeCreateScreen(
          role: widget.role,
          orderId: widget.orderId!,
          service: _service,
          uploader: _uploader,
        ),
      ),
    );
    if (created == true && mounted) await _load();
  }

  @override
  Widget build(BuildContext context) {
    // MOB-UI-16: a pushed `NordicPage`; "Khiếu nại ca này" in the bar (DESIGN-SYSTEM §5 List).
    return NordicPage.pushed(
      title: 'Khiếu nại',
      onRefresh: _load,
      actions: widget.orderId != null && !_loading && _error == null
          ? [
              NordicButton(
                key: const Key('dispute-new'),
                label: 'Khiếu nại ca này',
                onPressed: _startNew,
              ),
            ]
          : null,
      children: _body(),
    );
  }

  List<Widget> _body() {
    if (_loading) {
      return const [
        SizedBox(
          height: 320,
          child: LoadingStateWidget(message: 'Đang tải khiếu nại…'),
        ),
      ];
    }
    if (_error != null) {
      return [ErrorStateWidget(message: _error!, onRetry: _load)];
    }
    if (_items.isEmpty) {
      return const [
        EmptyStateWidget(
          key: Key('dispute-empty'),
          title: 'Chưa có khiếu nại nào',
          icon: Icons.flag_outlined,
        ),
      ];
    }
    return [for (final d in _items) _tile(d)];
  }

  PillTone _tone(String status) => switch (status) {
    Dispute.resolved => PillTone.ok,
    Dispute.dismissed => PillTone.neutral,
    _ => PillTone.warn,
  };

  /// ui-templates `customer/disputes.html`: sent → reviewed → verdict.
  Widget _steps(String status) {
    final index = switch (status) {
      Dispute.open => 0,
      Dispute.inReview => 1,
      _ => 2,
    };
    final finished = status == Dispute.resolved || status == Dispute.dismissed;
    const labels = ['Đã gửi', 'Xem xét', 'Kết luận'];
    return StepDots(
      steps: [
        for (var i = 0; i < labels.length; i++)
          StepItem(
            labels[i],
            finished || i < index
                ? DotState.done
                : (i == index ? DotState.current : DotState.todo),
          ),
      ],
    );
  }

  Widget _tile(Dispute d) {
    return Padding(
      padding: const EdgeInsets.only(bottom: NordicSpace.sm),
      child: NordicCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(
                  child: Text(
                    'Đơn #${d.orderId} · ${categoryLabel(d.category)}',
                    style: NordicTypography.titleSmall,
                  ),
                ),
                const SizedBox(width: NordicSpace.xs),
                Flexible(
                  child: StatusPill(
                    statusLabel(d.disputeStatus),
                    textKey: Key('dispute-status-${d.disputeId}'),
                    tone: _tone(d.disputeStatus),
                    dot: true,
                  ),
                ),
              ],
            ),
            const SizedBox(height: NordicSpace.xxs),
            Text(
              'Gửi lúc ${formatVietnamDateTime(d.createdAt)} · ${d.evidenceUrls.length} ảnh',
              style: NordicTypography.bodySmall,
            ),
            const SizedBox(height: NordicSpace.xs),
            Text(
              d.description,
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: NordicTypography.bodyRegular,
            ),
            const SizedBox(height: NordicSpace.md),
            _steps(d.disputeStatus),
            ..._outcome(d),
          ],
        ),
      ),
    );
  }

  /// The party sees the verdict only once it exists (contract 2.1).
  List<Widget> _outcome(Dispute d) {
    if (d.disputeStatus == Dispute.resolved) {
      return [
        const Divider(height: 32.0),
        Text(
          faultLabel(d.faultParty),
          key: Key('dispute-fault-${d.disputeId}'),
          style: NordicTypography.titleSmall,
        ),
        if (d.compensationAmount != null) ...[
          const SizedBox(height: NordicSpace.xxs),
          Text(
            'Bồi thường: ${formatVnd(d.compensationAmount!)}',
            key: Key('dispute-compensation-${d.disputeId}'),
            style: NordicTypography.numberSmall.copyWith(fontSize: 18.0),
          ),
        ],
      ];
    }
    if (d.disputeStatus == Dispute.dismissed) {
      return [
        const SizedBox(height: NordicSpace.sm),
        const Text(
          'Khiếu nại đã bị bác bỏ, không có khoản bồi thường.',
          style: NordicTypography.bodySmall,
        ),
      ];
    }
    return [
      const SizedBox(height: NordicSpace.sm),
      Row(
        children: [
          const Icon(
            Icons.timer_outlined,
            size: 16.0,
            color: NordicColors.textSecondary,
          ),
          const SizedBox(width: 6.0),
          Expanded(
            child: Text(
              'Hạn xử lý dự kiến: ${formatVietnamDateTime(d.slaDueAt)}',
              style: NordicTypography.bodySmall,
            ),
          ),
        ],
      ),
    ];
  }
}
