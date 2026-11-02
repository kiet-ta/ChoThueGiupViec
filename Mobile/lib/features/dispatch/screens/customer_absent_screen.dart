import 'dart:async';

import 'package:flutter/material.dart';

import '../../../core/motion/error_shake.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/progress_ring.dart';
import '../models/customer_absent_report.dart';
import '../services/dispatch_service.dart';

/// Screen/dialog for reporting customer absence after 15m wait & >=2 calls (MOB-M3-03, BR-05, Q10).
/// MOB-UI-22 (`Mobile/DESIGN-SYSTEM-WORKER.md`): `NordicPage.pushed`, the main button in `actions`,
/// tokens only, a failed step shakes its message.
class CustomerAbsentScreen extends StatefulWidget {
  final int assignmentId;
  final DateTime checkedInAt;
  final String customerPhoneMasked;
  final DispatchService? dispatchService;

  const CustomerAbsentScreen({
    super.key,
    required this.assignmentId,
    required this.checkedInAt,
    this.customerPhoneMasked = '090***4567',
    this.dispatchService,
  });

  @override
  State<CustomerAbsentScreen> createState() => _CustomerAbsentScreenState();
}

class _CustomerAbsentScreenState extends State<CustomerAbsentScreen> {
  late final DispatchService _dispatchService;
  late DateTime _checkedInAt;
  Timer? _timer;
  int _callAttempts = 0;
  bool _isLoggingCall = false;
  bool _isSubmittingReport = false;
  String? _errorMessage;
  CustomerAbsentReport? _absentReport;

  /// Bumped on every failure so the message shakes.
  int _errors = 0;

  @override
  void initState() {
    super.initState();
    _dispatchService = widget.dispatchService ?? DispatchService();
    _checkedInAt = widget.checkedInAt;
    _timer = Timer.periodic(const Duration(seconds: 10), (timer) {
      if (mounted) setState(() {});
    });
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  int get _elapsedMinutes => DateTime.now().difference(_checkedInAt).inMinutes;
  bool get _hasWaited15Minutes => _elapsedMinutes >= 15;
  bool get _hasMade2Calls => _callAttempts >= 2;
  bool get _canReportAbsent => _hasWaited15Minutes && _hasMade2Calls;

  Future<void> _handleCallCustomer() async {
    setState(() {
      _isLoggingCall = true;
      _errorMessage = null;
    });

    try {
      final result = await _dispatchService.logCallAttempt(widget.assignmentId);
      if (!mounted) return;
      setState(() {
        _callAttempts = result.callAttempts;
        _isLoggingCall = false;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _isLoggingCall = false;
        _errors++;
        _errorMessage = 'Chưa ghi nhận được cuộc gọi: $e';
      });
    }
  }

  Future<void> _handleReportAbsent() async {
    if (!_canReportAbsent) return;

    setState(() {
      _isSubmittingReport = true;
      _errorMessage = null;
    });

    try {
      final report = await _dispatchService.reportCustomerAbsent(
        widget.assignmentId,
      );
      if (!mounted) return;
      setState(() {
        _absentReport = report;
        _isSubmittingReport = false;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _isSubmittingReport = false;
        _errors++;
        _errorMessage = 'Chưa báo vắng mặt được: $e';
      });
    }
  }

  String _formatCurrency(double amount) {
    final int intAmount = amount.round();
    final buffer = StringBuffer();
    final str = intAmount.toString();
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
    final report = _absentReport;
    return NordicPage.pushed(
      title: 'Báo gia chủ vắng',
      actions: [
        if (report == null)
          NordicButton(
            key: const Key('report_absent_button'),
            label: _canReportAbsent
                ? 'Báo vắng mặt (40%)'
                : 'Chưa đủ điều kiện',
            variant: _canReportAbsent
                ? NordicButtonVariant.primary
                : NordicButtonVariant.secondary,
            isLoading: _isSubmittingReport,
            onPressed: _canReportAbsent && !_isSubmittingReport
                ? _handleReportAbsent
                : null,
          )
        else
          NordicButton(
            key: const Key('finish_absent_screen_button'),
            label: 'Hoàn tất',
            onPressed: () {
              Navigator.of(context).maybePop();
            },
          ),
      ],
      children: report != null
          ? _buildSuccessView(report)
          : _buildReportingView(),
    );
  }

  Widget _conditionRow({
    required bool done,
    required IconData pendingIcon,
    required String title,
    required String detail,
  }) {
    return Row(
      children: [
        Icon(
          done ? Icons.check_circle : pendingIcon,
          color: done ? NordicColors.success : NordicColors.accentAmber,
          size: 28.0,
        ),
        const SizedBox(width: NordicSpace.sm),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(title, style: NordicTypography.titleSmall),
              const SizedBox(height: NordicSpace.xxs),
              Text(detail, style: NordicTypography.bodySmall),
            ],
          ),
        ),
      ],
    );
  }

  List<Widget> _buildReportingView() {
    final h = _checkedInAt.hour.toString().padLeft(2, '0');
    final m = _checkedInAt.minute.toString().padLeft(2, '0');
    return [
      const Text('Gia chủ chưa mở cửa?', style: NordicTypography.h2),
      const SizedBox(height: NordicSpace.xxs),
      const Text(
        'Để nhận bồi hoàn 40% giá trị ca, bạn cần làm đủ 2 việc bên dưới.',
        style: NordicTypography.bodyRegular,
      ),
      const SizedBox(height: NordicSpace.md),
      Row(
        children: [
          Expanded(
            child: _ringCard(
              value: _elapsedMinutes / 15,
              big: '$_elapsedMinutes',
              unit: '/ 15 phút',
              label: 'Đã chờ',
            ),
          ),
          const SizedBox(width: NordicSpace.sm),
          Expanded(
            child: _ringCard(
              value: _callAttempts / 2,
              big: '$_callAttempts',
              unit: '/ 2 lần',
              label: 'Đã gọi',
            ),
          ),
        ],
      ),
      const SizedBox(height: NordicSpace.sm),

      // Precondition 1: Wait >= 15 minutes
      NordicCard(
        isHighlighted: _hasWaited15Minutes,
        child: _conditionRow(
          done: _hasWaited15Minutes,
          pendingIcon: Icons.hourglass_top_outlined,
          title: '1. Chờ ít nhất 15 phút tại nơi làm',
          detail: 'Đã chờ: $_elapsedMinutes / 15 phút (từ $h:$m)',
        ),
      ),
      const SizedBox(height: NordicSpace.sm),

      // Precondition 2: At least 2 call attempts
      NordicCard(
        isHighlighted: _hasMade2Calls,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            _conditionRow(
              done: _hasMade2Calls,
              pendingIcon: Icons.phone_callback_outlined,
              title: '2. Gọi ít nhất 2 cuộc qua ứng dụng',
              detail: 'Số cuộc gọi đã thực hiện: $_callAttempts / 2',
            ),
            const SizedBox(height: NordicSpace.sm),
            NordicButton(
              key: const Key('call_customer_button'),
              variant: NordicButtonVariant.secondary,
              icon: const Icon(Icons.phone, size: 18.0),
              label: _isLoggingCall
                  ? 'Đang kết nối...'
                  : 'Gọi gia chủ (${widget.customerPhoneMasked})',
              onPressed: _isLoggingCall ? null : _handleCallCustomer,
            ),
          ],
        ),
      ),
      const SizedBox(height: NordicSpace.md),

      ErrorShake(
        errors: _errors,
        child: _errorMessage == null
            ? const SizedBox.shrink()
            : Container(
                padding: const EdgeInsets.all(NordicSpace.sm),
                decoration: BoxDecoration(
                  color: NordicColors.errorContainer,
                  borderRadius: NordicRadius.fieldAll,
                  border: Border.all(color: NordicColors.error),
                ),
                child: Text(
                  _errorMessage!,
                  style: NordicTypography.bodySmall.copyWith(
                    color: NordicColors.error,
                  ),
                ),
              ),
      ),
    ];
  }

  /// A card with a [ProgressRing]: the big number inside, the small unit under it, a label below.
  Widget _ringCard({
    required double value,
    required String big,
    required String unit,
    required String label,
  }) {
    return NordicCard(
      child: Column(
        children: [
          ProgressRing(
            value: value,
            size: 104,
            stroke: 9,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(big, style: NordicTypography.numberMedium),
                Text(unit, style: NordicTypography.bodySmall),
              ],
            ),
          ),
          const SizedBox(height: NordicSpace.xs),
          Text(label, style: NordicTypography.titleSmall),
        ],
      ),
    );
  }

  Widget _summaryRow(String label, String value, {TextStyle? valueStyle}) {
    return Row(
      mainAxisAlignment: MainAxisAlignment.spaceBetween,
      children: [
        Expanded(child: Text(label, style: NordicTypography.bodySmall)),
        const SizedBox(width: NordicSpace.xs),
        Text(value, style: valueStyle ?? NordicTypography.titleSmall),
      ],
    );
  }

  List<Widget> _buildSuccessView(CustomerAbsentReport report) {
    return [
      const SizedBox(height: NordicSpace.md),
      const Icon(
        Icons.verified_outlined,
        size: 64.0,
        color: NordicColors.success,
      ),
      const SizedBox(height: NordicSpace.md),
      Text(
        'Đã gửi báo cáo gia chủ vắng',
        textAlign: TextAlign.center,
        style: NordicTypography.h2.copyWith(color: NordicColors.primary),
      ),
      const SizedBox(height: NordicSpace.xs),
      const Text(
        'Báo cáo đã được chuyển tới quản trị viên để duyệt bồi hoàn.',
        textAlign: TextAlign.center,
        style: NordicTypography.bodyRegular,
      ),
      const SizedBox(height: NordicSpace.lg),

      NordicCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const Text('Chi tiết bồi hoàn', style: NordicTypography.titleSmall),
            const SizedBox(height: NordicSpace.sm),
            _summaryRow('Thời gian chờ', '${report.elapsedMinutes} phút'),
            const SizedBox(height: NordicSpace.xs),
            _summaryRow('Cuộc gọi liên hệ', '${report.callAttempts} cuộc'),
            const SizedBox(height: NordicSpace.xs),
            const Divider(color: NordicColors.border, height: 1.0),
            const SizedBox(height: NordicSpace.xs),
            _summaryRow(
              'Tỷ lệ bồi hoàn',
              '${(report.workerFeeRate * 100).round()}%',
            ),
            const SizedBox(height: NordicSpace.xs),
            _summaryRow(
              'Số tiền bạn nhận',
              _formatCurrency(report.absenceFeeAmount),
              valueStyle: NordicTypography.titleMedium.copyWith(
                color: NordicColors.primary,
              ),
            ),
          ],
        ),
      ),
    ];
  }
}
