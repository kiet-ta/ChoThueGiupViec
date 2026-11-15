import 'package:flutter/material.dart';

import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/section_title.dart';
import '../../../core/widgets/status_pill.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../../../core/widgets/step_dots.dart';
import '../../payouts/logic/payout_logic.dart';
import '../logic/dispute_logic.dart';
import '../models/dispute_models.dart';
import '../services/disputes_service.dart';
import '../widgets/evidence_thumbnail.dart';

/// One dispute of the caller (DISPUTE-DETAIL-01): `GET .../disputes/{id}` (contract disputes.md 2.1). Opens with the
/// dispute of the list and asks the server again (pull to refresh too); a 404 says it is not found.
class DisputeDetailScreen extends StatefulWidget {
  final Dispute dispute;
  final DisputeRole role;
  final IDisputesService service;

  const DisputeDetailScreen({
    super.key,
    required this.dispute,
    required this.role,
    required this.service,
  });

  @override
  State<DisputeDetailScreen> createState() => _DisputeDetailScreenState();
}

class _DisputeDetailScreenState extends State<DisputeDetailScreen> {
  late Dispute _dispute = widget.dispute;
  String? _error;

  @override
  void initState() {
    super.initState();
    _reload();
  }

  Future<void> _reload() async {
    try {
      final fresh = await widget.service.getMine(
        widget.role,
        widget.dispute.disputeId,
      );
      if (!mounted) return;
      setState(() {
        _dispute = fresh;
        _error = null;
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(
        () => _error = e.statusCode == 404
            ? 'Không tìm thấy khiếu nại này.'
            : 'Chưa tải lại được khiếu nại. Kéo xuống để thử lại.',
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final d = _dispute;
    return NordicPage.pushed(
      title: 'Chi tiết khiếu nại',
      onRefresh: _reload,
      children: [
        if (_error == 'Không tìm thấy khiếu nại này.')
          ErrorStateWidget(message: _error!, onRetry: _reload)
        else ...[
          if (_error != null) ...[
            Text(
              _error!,
              key: const Key('dispute-detail-error'),
              style: NordicTypography.bodySmall.copyWith(
                color: NordicColors.error,
              ),
            ),
            const SizedBox(height: NordicSpace.sm),
          ],
          _header(d),
          const SectionTitle('Nội dung'),
          NordicCard(
            child: Text(
              d.description,
              key: const Key('dispute-detail-description'),
              style: NordicTypography.bodyRegular,
            ),
          ),
          SectionTitle(
            'Ảnh bằng chứng',
            trailing: '${d.evidenceUrls.length} ảnh',
          ),
          _photos(d),
          const SectionTitle('Tiến trình'),
          NordicCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [_steps(d.disputeStatus), ..._outcome(d)],
            ),
          ),
        ],
      ],
    );
  }

  Widget _header(Dispute d) {
    return NordicCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: Text(
                  '#D-${d.disputeId}',
                  style: NordicTypography.titleMedium,
                ),
              ),
              StatusPill(
                statusLabel(d.disputeStatus),
                textKey: const Key('dispute-detail-status'),
                tone: _tone(d.disputeStatus),
                dot: true,
              ),
            ],
          ),
          const SizedBox(height: NordicSpace.xs),
          Text(categoryLabel(d.category), style: NordicTypography.titleSmall),
          const SizedBox(height: NordicSpace.xxs),
          Text(
            'Đơn #${d.orderId} · gửi lúc ${formatVietnamDateTime(d.createdAt)}',
            style: NordicTypography.bodySmall,
          ),
        ],
      ),
    );
  }

  Widget _photos(Dispute d) {
    if (d.evidenceUrls.isEmpty) {
      return const NordicCard(
        child: Text('Không có ảnh.', style: NordicTypography.bodySmall),
      );
    }
    return GridView.count(
      crossAxisCount: 3,
      shrinkWrap: true,
      physics: const NeverScrollableScrollPhysics(),
      mainAxisSpacing: NordicSpace.xs,
      crossAxisSpacing: NordicSpace.xs,
      children: [
        for (var i = 0; i < d.evidenceUrls.length; i++)
          Semantics(
            button: true,
            label: 'Xem ảnh bằng chứng ${i + 1}',
            child: GestureDetector(
              key: Key('dispute-detail-photo-$i'),
              onTap: () => _openPhoto(d.evidenceUrls[i], i),
              child: EvidenceThumbnail(url: d.evidenceUrls[i], index: i),
            ),
          ),
      ],
    );
  }

  void _openPhoto(String url, int index) {
    final source = evidenceImageUrl(url);
    if (source == null) return;
    Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => Scaffold(
          backgroundColor: NordicColors.contrastDark,
          appBar: AppBar(
            backgroundColor: NordicColors.contrastDark,
            foregroundColor: NordicColors.textInverse,
            title: Text('Ảnh ${index + 1}'),
          ),
          body: Center(
            child: InteractiveViewer(
              child: Image.network(
                source,
                semanticLabel: 'Ảnh bằng chứng ${index + 1}',
              ),
            ),
          ),
        ),
      ),
    );
  }

  PillTone _tone(String status) => switch (status) {
    Dispute.resolved => PillTone.ok,
    Dispute.dismissed => PillTone.neutral,
    _ => PillTone.warn,
  };

  /// Sent → reviewed → verdict, like the list.
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

  String _verdict(String? faultParty) => widget.role == DisputeRole.customer
      ? switch (faultParty) {
          'FREELANCER' => 'Lỗi thuộc về người làm',
          'AGENCY' => 'Lỗi thuộc về công ty đối tác',
          _ => faultLabel(faultParty),
        }
      : faultLabel(faultParty);

  List<Widget> _outcome(Dispute d) {
    if (d.disputeStatus == Dispute.resolved) {
      return [
        const Divider(height: 32.0),
        Text(
          _verdict(d.faultParty),
          key: const Key('dispute-detail-fault'),
          style: NordicTypography.titleSmall,
        ),
        const SizedBox(height: NordicSpace.xxs),
        Text(
          d.compensationAmount != null
              ? 'Bồi thường: ${formatVnd(d.compensationAmount!)}'
              : 'Không có khoản bồi thường.',
          key: const Key('dispute-detail-compensation'),
          style: NordicTypography.bodyRegular,
        ),
        if (d.resolvedAt != null) ...[
          const SizedBox(height: NordicSpace.xxs),
          Text(
            'Kết luận lúc ${formatVietnamDateTime(d.resolvedAt)}',
            style: NordicTypography.bodySmall,
          ),
        ],
      ];
    }
    if (d.disputeStatus == Dispute.dismissed) {
      return const [
        SizedBox(height: NordicSpace.sm),
        Text(
          'Khiếu nại đã bị bác bỏ, không có khoản bồi thường.',
          style: NordicTypography.bodySmall,
        ),
      ];
    }
    return [
      const SizedBox(height: NordicSpace.sm),
      Text(
        'Hạn xử lý dự kiến: ${formatVietnamDateTime(d.slaDueAt)}',
        key: const Key('dispute-detail-sla'),
        style: NordicTypography.bodySmall,
      ),
    ];
  }
}
