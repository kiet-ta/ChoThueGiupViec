import 'package:flutter/material.dart';
import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/eyebrow_badge.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/status_state_widget.dart';
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

  const DisputeScreen({super.key, required this.role, this.orderId, this.service, this.uploader});

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
        builder: (_) => DisputeCreateScreen(role: widget.role, orderId: widget.orderId!, service: _service, uploader: _uploader),
      ),
    );
    if (created == true && mounted) await _load();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(title: const Text('Khiếu Nại & Tranh Chấp')),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: _body(),
        ),
      ),
    );
  }

  Widget _body() {
    if (_loading) return const LoadingStateWidget(message: 'Đang tải khiếu nại…');
    if (_error != null) return ErrorStateWidget(message: _error!, onRetry: _load);
    return ListView(
      padding: const EdgeInsets.all(16.0),
      children: [
        const EyebrowBadge(text: 'Bảo Vệ Quyền Lợi'),
        const SizedBox(height: 12.0),
        if (widget.orderId != null) ...[
          NordicButton(key: const Key('dispute-new'), label: 'Khiếu nại ca này', onPressed: _startNew),
          const SizedBox(height: 16.0),
        ],
        if (_items.isEmpty)
          const NordicCard(
            child: Text('Bạn chưa có khiếu nại nào.', key: Key('dispute-empty'), style: NordicTypography.bodyRegular),
          )
        else
          for (final d in _items) _tile(d),
      ],
    );
  }

  Widget _tile(Dispute d) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 8.0),
      child: NordicCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(child: Text('Đơn #${d.orderId} · ${categoryLabel(d.category)}', style: NordicTypography.bodyLarge)),
                const SizedBox(width: 8.0),
                Text(statusLabel(d.disputeStatus), key: Key('dispute-status-${d.disputeId}'), style: NordicTypography.labelMedium),
              ],
            ),
            const SizedBox(height: 6.0),
            Text(d.description, maxLines: 3, overflow: TextOverflow.ellipsis, style: NordicTypography.bodyRegular),
            const SizedBox(height: 6.0),
            Text('Gửi lúc ${formatVietnamDateTime(d.createdAt)} · ${d.evidenceUrls.length} ảnh', style: NordicTypography.bodySmall),
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
        const SizedBox(height: 8.0),
        Text(faultLabel(d.faultParty), key: Key('dispute-fault-${d.disputeId}'), style: NordicTypography.bodyLarge),
        if (d.compensationAmount != null)
          Text('Bồi thường: ${formatVnd(d.compensationAmount!)}', key: Key('dispute-compensation-${d.disputeId}'), style: NordicTypography.bodyRegular),
      ];
    }
    if (d.disputeStatus == Dispute.dismissed) {
      return [
        const SizedBox(height: 8.0),
        const Text('Khiếu nại đã bị bác bỏ, không có khoản bồi thường.', style: NordicTypography.bodyRegular),
      ];
    }
    return [
      const SizedBox(height: 8.0),
      Text('Hạn xử lý dự kiến: ${formatVietnamDateTime(d.slaDueAt)}', style: NordicTypography.bodySmall),
    ];
  }
}
