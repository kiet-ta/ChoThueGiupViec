import 'package:flutter/material.dart';
import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/eyebrow_badge.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/status_state_widget.dart';
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

  const PayoutScreen({super.key, this.service, this.now});

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

  void _go(int delta) {
    setState(() => _month = shiftMonth(_month, delta));
    _load();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(title: const Text('Thu Nhập & Quyết Toán')),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: ListView(
            padding: const EdgeInsets.symmetric(horizontal: 16.0, vertical: 16.0),
            children: [
              const EyebrowBadge(text: 'Thu Nhập Minh Bạch'),
              const SizedBox(height: 12.0),
              _monthSelector(),
              const SizedBox(height: 16.0),
              ..._content(),
            ],
          ),
        ),
      ),
    );
  }

  Widget _monthSelector() {
    return Row(
      mainAxisAlignment: MainAxisAlignment.spaceBetween,
      children: [
        IconButton(
          key: const Key('earnings-prev'),
          tooltip: 'Tháng trước',
          onPressed: canGoPrevious(_month) ? () => _go(-1) : null,
          icon: const Icon(Icons.chevron_left_rounded),
        ),
        Text(monthLabel(_month), key: const Key('earnings-month'), style: NordicTypography.h3),
        IconButton(
          key: const Key('earnings-next'),
          tooltip: 'Tháng sau',
          onPressed: canGoNext(_month, _currentMonth) ? () => _go(1) : null,
          icon: const Icon(Icons.chevron_right_rounded),
        ),
      ],
    );
  }

  List<Widget> _content() {
    if (_loading) return const [SizedBox(height: 240.0, child: LoadingStateWidget(message: 'Đang tải thu nhập…'))];
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
      const SizedBox(height: 12.0),
      _breakdown(e),
      const SizedBox(height: 16.0),
      Text('Ca làm trong tháng (${e.jobCount})', style: NordicTypography.h3),
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
          MaterialPageRoute<void>(builder: (_) => PayoutHistoryScreen(service: _service)),
        ),
      ),
    ];
  }

  Widget _summary(Earnings e) {
    return NordicCard(
      isHighlighted: true,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text('Thu nhập tháng này', style: NordicTypography.labelMedium),
          const SizedBox(height: 4.0),
          Text(formatVnd(e.netAmount), key: const Key('earnings-net'), style: NordicTypography.priceHighlight),
          const SizedBox(height: 12.0),
          Text(payoutStatusLabel(e.payoutStatus), key: const Key('earnings-status'), style: NordicTypography.bodyLarge),
          const SizedBox(height: 2.0),
          Text(payoutStatusHint(e.payoutStatus), style: NordicTypography.bodySmall),
        ],
      ),
    );
  }

  Widget _breakdown(Earnings e) {
    Widget row(String label, String value, {Key? key}) => Padding(
          padding: const EdgeInsets.symmetric(vertical: 4.0),
          child: Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Expanded(child: Text(label, style: NordicTypography.bodyRegular)),
              const SizedBox(width: 8.0),
              Text(value, key: key, style: NordicTypography.bodyRegular),
            ],
          ),
        );
    return NordicCard(
      child: Column(
        children: [
          row('Tổng tiền công', formatVnd(e.grossAmount), key: const Key('earnings-gross')),
          row('Hoa hồng nền tảng', '-${formatVnd(e.commissionAmount)}', key: const Key('earnings-commission')),
          row('Khấu trừ khiếu nại', '-${formatVnd(e.penaltyAmount)}', key: const Key('earnings-penalty')),
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
                  Text('Đơn #${job.orderId}', style: NordicTypography.bodyLarge),
                  const SizedBox(height: 2.0),
                  Text(formatVietnamDateTime(job.completedAt), style: NordicTypography.bodySmall),
                  const SizedBox(height: 2.0),
                  Text(
                    'Tiền công ${formatVnd(job.grossAmount)} · Hoa hồng ${formatVnd(job.commissionAmount)}',
                    style: NordicTypography.bodySmall,
                  ),
                  if (job.absenceFee)
                    Padding(
                      padding: const EdgeInsets.only(top: 6.0),
                      child: Container(
                        padding: const EdgeInsets.symmetric(horizontal: 8.0, vertical: 2.0),
                        decoration: BoxDecoration(
                          color: NordicColors.accentAmber.withValues(alpha: 0.16),
                          borderRadius: BorderRadius.circular(9999.0),
                        ),
                        child: const Text('Phí vắng mặt', style: NordicTypography.labelSmall),
                      ),
                    ),
                ],
              ),
            ),
            const SizedBox(width: 8.0),
            Text(formatVnd(job.netAmount), key: Key('job-net-${job.assignmentId}'), style: NordicTypography.bodyLarge),
          ],
        ),
      ),
    );
  }
}
