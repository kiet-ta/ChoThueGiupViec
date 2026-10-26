import 'package:flutter/material.dart';

import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/status_pill.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../../disputes/models/dispute_models.dart';
import '../../disputes/routes.dart';
import '../../disputes/screens/dispute_screen.dart';
import '../../ratings/models/rating_models.dart';
import '../../ratings/routes.dart';
import '../../ratings/screens/rating_screen.dart';
import '../logic/payout_logic.dart';
import '../models/payout_models.dart';
import '../services/payouts_service.dart';
import 'payout_history_screen.dart';

/// Worker income of one month (MOB-M6-04); contract payouts.md 2.5.
/// There is no wallet (open question P6): the figure is "Thu nhập tháng này", paid once a month in a batch (Q04).
class PayoutScreen extends StatefulWidget {
  final IPayoutsService? service;

  /// The clock, so the default month can be tested; the real screen uses the device time.
  final DateTime Function()? now;

  /// What the two buttons of a job do; by default they open the rating and dispute screens of the Ratings and Disputes features
  /// (nothing else in the app opens them yet: the booking and tracking screens belong to other slots).
  final void Function(EarningsJob job)? onRate;
  final void Function(EarningsJob job)? onDispute;

  const PayoutScreen({
    super.key,
    this.service,
    this.now,
    this.onRate,
    this.onDispute,
  });

  @override
  State<PayoutScreen> createState() => _PayoutScreenState();
}

class _PayoutScreenState extends State<PayoutScreen> {
  late final IPayoutsService _service;
  late final String _currentMonth;
  late String _month;

  Earnings? _earnings;
  String? _error;
  bool _forbidden = false;
  bool _loading = true;

  /// Answers of an older month must not overwrite a newer choice.
  int _requestId = 0;

  @override
  void initState() {
    super.initState();
    _service = widget.service ?? PayoutsService();
    _currentMonth = monthKeyOf((widget.now ?? DateTime.now)());
    _month = _currentMonth;
    _load();
  }

  Future<void> _load() async {
    final id = ++_requestId;
    setState(() {
      _loading = true;
      _error = null;
      _forbidden = false;
    });
    try {
      final earnings = await _service.getEarnings(_month);
      if (!mounted || id != _requestId) return;
      setState(() {
        _earnings = earnings;
        _loading = false;
      });
    } on ApiException catch (e) {
      if (!mounted || id != _requestId) return;
      setState(() {
        _earnings = null;
        _error = earningsFailureMessage(e);
        _forbidden = e.isForbidden;
        _loading = false;
      });
    }
  }

  void _rate(EarningsJob job) {
    if (widget.onRate != null) return widget.onRate!(job);
    Navigator.of(context).pushNamed(
      RatingRoutes.ratings,
      arguments: RatingScreenArgs(
        role: RatingRole.worker,
        assignmentId: job.assignmentId,
      ),
    );
  }

  void _dispute(EarningsJob job) {
    if (widget.onDispute != null) return widget.onDispute!(job);
    Navigator.of(context).pushNamed(
      DisputeRoutes.disputes,
      arguments: DisputeScreenArgs(
        role: DisputeRole.worker,
        orderId: job.orderId,
      ),
    );
  }

  void _go(int delta) {
    setState(() => _month = shiftMonth(_month, delta));
    _load();
  }

  @override
  Widget build(BuildContext context) {
    final pushed = ModalRoute.of(context)?.canPop ?? false;
    return Scaffold(
      appBar: pushed ? AppBar(title: const Text('Thu nhập')) : null,
      body: SafeArea(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 390.0),
            child: ListView(
              padding: const EdgeInsets.fromLTRB(20, 8, 20, 32),
              children: [
                if (!pushed)
                  const Padding(
                    padding: EdgeInsets.only(top: 8, bottom: 16),
                    child: Text(
                      'Thu nhập',
                      style: NordicTypography.screenTitle,
                    ),
                  ),
                _monthSelector(),
                const SizedBox(height: 16.0),
                ..._content(),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Widget _monthSelector() {
    return Row(
      mainAxisAlignment: MainAxisAlignment.spaceBetween,
      children: [
        IconButton.outlined(
          key: const Key('earnings-prev'),
          tooltip: 'Tháng trước',
          onPressed: canGoPrevious(_month) ? () => _go(-1) : null,
          icon: const Icon(Icons.chevron_left_rounded),
        ),
        Text(
          monthLabel(_month),
          key: const Key('earnings-month'),
          style: NordicTypography.titleSmall,
        ),
        IconButton.outlined(
          key: const Key('earnings-next'),
          tooltip: 'Tháng sau',
          onPressed: canGoNext(_month, _currentMonth) ? () => _go(1) : null,
          icon: const Icon(Icons.chevron_right_rounded),
        ),
      ],
    );
  }

  List<Widget> _content() {
    if (_loading) {
      return const [
        SizedBox(
          height: 240.0,
          child: LoadingStateWidget(message: 'Đang tải thu nhập…'),
        ),
      ];
    }
    if (_error != null) {
      return [
        ErrorStateWidget(
          message: _error!,
          // Retrying does not help an agency staff member; the explanation is the whole answer.
          onRetry: _forbidden ? null : _load,
        ),
      ];
    }
    final e = _earnings!;
    return [
      _summary(e),
      const SizedBox(height: 20.0),
      _breakdown(e),
      const SizedBox(height: 24.0),
      Row(
        children: [
          const Text('Ca trong tháng', style: NordicTypography.titleSmall),
          const Spacer(),
          Text('${e.jobCount}', style: NordicTypography.bodySmall),
        ],
      ),
      const SizedBox(height: 8.0),
      if (e.jobs.isEmpty)
        const NordicCard(
          child: Text(
            'Tháng này chưa có ca làm hoàn thành.',
            key: Key('earnings-empty'),
            style: NordicTypography.bodyRegular,
          ),
        )
      else
        for (final job in e.jobs) _jobTile(job),
      const SizedBox(height: 16.0),
      NordicButton(
        key: const Key('earnings-history'),
        label: 'Lịch sử giải ngân',
        variant: NordicButtonVariant.secondary,
        onPressed: () => Navigator.of(context).push(
          MaterialPageRoute<void>(
            builder: (_) => PayoutHistoryScreen(service: _service),
          ),
        ),
      ),
    ];
  }

  /// `.center` block: the caption, the net amount as the one big number and the payout status as a pill.
  Widget _summary(Earnings e) {
    return Column(
      children: [
        const Text('Thu nhập tháng này', style: NordicTypography.bodySmall),
        const SizedBox(height: 8.0),
        Text(
          formatVnd(e.netAmount),
          key: const Key('earnings-net'),
          style: NordicTypography.numberLarge,
        ),
        const SizedBox(height: 8.0),
        StatusPill(
          payoutStatusLabel(e.payoutStatus),
          dot: true,
          tone: e.payoutStatus == 'TRANSFERRED'
              ? PillTone.ok
              : PillTone.neutral,
          textKey: const Key('earnings-status'),
        ),
        const SizedBox(height: 8.0),
        Text(
          payoutStatusHint(e.payoutStatus),
          textAlign: TextAlign.center,
          style: NordicTypography.bodySmall,
        ),
      ],
    );
  }

  /// `.card` with a split bar (net against commission, penalty when there is one) and the three figures.
  Widget _breakdown(Earnings e) {
    Widget row(String label, String value, Color swatch, {Key? key}) => Padding(
      padding: const EdgeInsets.symmetric(vertical: 4.0),
      child: Row(
        children: [
          Container(
            width: 10,
            height: 10,
            decoration: BoxDecoration(
              color: swatch,
              borderRadius: BorderRadius.circular(3),
            ),
          ),
          const SizedBox(width: 8.0),
          Expanded(child: Text(label, style: NordicTypography.bodyRegular)),
          const SizedBox(width: 8.0),
          Text(value, key: key, style: NordicTypography.titleSmall),
        ],
      ),
    );
    final total = e.netAmount + e.commissionAmount + e.penaltyAmount;
    int flex(int part) => total <= 0 ? 0 : (part * 1000 ~/ total);
    return NordicCard(
      child: Column(
        children: [
          if (total > 0)
            ClipRRect(
              borderRadius: BorderRadius.circular(6.0),
              child: Row(
                children: [
                  for (final (part, color) in [
                    (e.netAmount, NordicColors.primary),
                    (e.commissionAmount, NordicColors.accentAmber),
                    (e.penaltyAmount, NordicColors.error),
                  ])
                    if (flex(part) > 0)
                      Expanded(
                        flex: flex(part),
                        child: Container(
                          height: 12.0,
                          margin: const EdgeInsets.only(right: 2.0),
                          color: color,
                        ),
                      ),
                ],
              ),
            ),
          const SizedBox(height: 8.0),
          row(
            'Tổng tiền công',
            formatVnd(e.grossAmount),
            NordicColors.surfaceDim,
            key: const Key('earnings-gross'),
          ),
          row(
            'Hoa hồng nền tảng',
            '-${formatVnd(e.commissionAmount)}',
            NordicColors.accentAmber,
            key: const Key('earnings-commission'),
          ),
          row(
            'Khấu trừ khiếu nại',
            '-${formatVnd(e.penaltyAmount)}',
            NordicColors.error,
            key: const Key('earnings-penalty'),
          ),
        ],
      ),
    );
  }

  Widget _jobTile(EarningsJob job) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 8.0),
      child: NordicCard(
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    'Đơn #${job.orderId}',
                    style: NordicTypography.titleSmall,
                  ),
                  const SizedBox(height: 2.0),
                  Text(
                    formatVietnamDateTime(job.completedAt),
                    style: NordicTypography.bodySmall,
                  ),
                  const SizedBox(height: 2.0),
                  Text(
                    'Tiền công ${formatVnd(job.grossAmount)} · Hoa hồng ${formatVnd(job.commissionAmount)}',
                    style: NordicTypography.bodySmall,
                  ),
                  if (!job.absenceFee)
                    Wrap(
                      spacing: 8.0,
                      children: [
                        TextButton(
                          key: Key('job-rate-${job.assignmentId}'),
                          onPressed: () => _rate(job),
                          child: const Text('Đánh giá khách'),
                        ),
                        TextButton(
                          key: Key('job-dispute-${job.assignmentId}'),
                          onPressed: () => _dispute(job),
                          child: const Text('Khiếu nại'),
                        ),
                      ],
                    ),
                  if (job.absenceFee)
                    const Padding(
                      padding: EdgeInsets.only(top: 6.0),
                      child: StatusPill('Phí vắng mặt', tone: PillTone.warn),
                    ),
                ],
              ),
            ),
            const SizedBox(width: 8.0),
            Text(
              formatVnd(job.netAmount),
              key: Key('job-net-${job.assignmentId}'),
              style: NordicTypography.titleSmall,
            ),
          ],
        ),
      ),
    );
  }
}
