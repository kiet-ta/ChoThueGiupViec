import 'dart:async';
import 'package:flutter/material.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/eyebrow_badge.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../models/customer_absent_report.dart';
import '../services/dispatch_service.dart';

/// Screen/dialog for reporting customer absence after 15m wait & >=2 calls (MOB-M3-03, BR-05, Q10).
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
        _errorMessage = 'Ghi nhận cuộc gọi thất bại: $e';
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
      final report = await _dispatchService.reportCustomerAbsent(widget.assignmentId);
      if (!mounted) return;
      setState(() {
        _absentReport = report;
        _isSubmittingReport = false;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _isSubmittingReport = false;
        _errorMessage = 'Báo vắng mặt không thành công: $e';
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
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(
        title: const Text('Báo Khách Vắng Mặt'),
      ),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: _absentReport != null ? _buildSuccessView() : _buildReportingView(),
        ),
      ),
    );
  }

  Widget _buildReportingView() {
    return ListView(
      padding: const EdgeInsets.all(16.0),
      children: [
        const EyebrowBadge(text: 'HIỆN TRƯỜNG (BR-05)'),
        const SizedBox(height: 8.0),
        const Text(
          'Khách hàng chưa mở cửa?',
          style: NordicTypography.h2,
        ),
        const SizedBox(height: 4.0),
        Text(
          'Để đảm bảo quyền lợi và nhận bồi hoàn 40% giá trị ca, vui lòng tuân thủ 2 điều kiện bên dưới.',
          style: NordicTypography.bodyRegular,
        ),
        const SizedBox(height: 16.0),

        // Precondition 1: Wait >= 15 minutes
        NordicCard(
          isHighlighted: _hasWaited15Minutes,
          child: Padding(
            padding: const EdgeInsets.all(16.0),
            child: Row(
              children: [
                Icon(
                  _hasWaited15Minutes
                      ? Icons.check_circle
                      : Icons.hourglass_top_outlined,
                  color: _hasWaited15Minutes
                      ? NordicColors.success
                      : NordicColors.accentAmber,
                  size: 28.0,
                ),
                const SizedBox(width: 12.0),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        '1. Chờ tối thiểu 15 phút tại nơi làm',
                        style: NordicTypography.labelMedium,
                      ),
                      const SizedBox(height: 4.0),
                      Text(
                        'Đã chờ: $_elapsedMinutes / 15 phút (từ ${_checkedInAt.hour.toString().padLeft(2, '0')}:${_checkedInAt.minute.toString().padLeft(2, '0')})',
                        style: NordicTypography.bodySmall,
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
        const SizedBox(height: 12.0),

        // Precondition 2: At least 2 call attempts
        NordicCard(
          isHighlighted: _hasMade2Calls,
          child: Padding(
            padding: const EdgeInsets.all(16.0),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Row(
                  children: [
                    Icon(
                      _hasMade2Calls ? Icons.check_circle : Icons.phone_callback_outlined,
                      color: _hasMade2Calls ? NordicColors.success : NordicColors.accentAmber,
                      size: 28.0,
                    ),
                    const SizedBox(width: 12.0),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            '2. Gọi ít nhất 2 cuộc qua hệ thống',
                            style: NordicTypography.labelMedium,
                          ),
                          const SizedBox(height: 4.0),
                          Text(
                            'Số cuộc gọi đã thực hiện: $_callAttempts / 2',
                            style: NordicTypography.bodySmall,
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 12.0),
                OutlinedButton.icon(
                  key: const Key('call_customer_button'),
                  onPressed: _isLoggingCall ? null : _handleCallCustomer,
                  icon: const Icon(Icons.phone),
                  label: Text(
                    _isLoggingCall
                        ? 'Đang kết nối...'
                        : 'Gọi khách (${widget.customerPhoneMasked})',
                  ),
                ),
              ],
            ),
          ),
        ),
        const SizedBox(height: 16.0),

        if (_errorMessage != null) ...[
          Container(
            padding: const EdgeInsets.all(12.0),
            margin: const EdgeInsets.only(bottom: 16.0),
            decoration: BoxDecoration(
              color: NordicColors.errorContainer,
              borderRadius: BorderRadius.circular(8.0),
              border: Border.all(color: NordicColors.error),
            ),
            child: Text(
              _errorMessage!,
              style: NordicTypography.bodySmall.copyWith(color: NordicColors.error),
            ),
          ),
        ],

        // Submit action
        NordicButton(
          key: const Key('report_absent_button'),
          label: _canReportAbsent ? 'Báo Vắng Mặt (40%)' : 'Chưa đủ điều kiện',
          variant: _canReportAbsent ? NordicButtonVariant.primary : NordicButtonVariant.secondary,
          isLoading: _isSubmittingReport,
          onPressed: _canReportAbsent && !_isSubmittingReport ? _handleReportAbsent : null,
        ),
      ],
    );
  }

  Widget _buildSuccessView() {
    final report = _absentReport!;

    return ListView(
      padding: const EdgeInsets.all(24.0),
      children: [
        const Icon(
          Icons.verified_outlined,
          size: 64.0,
          color: NordicColors.success,
        ),
        const SizedBox(height: 16.0),
        Text(
          'Đã Gửi Báo Cáo Khách Vắng Mặt',
          textAlign: TextAlign.center,
          style: NordicTypography.h2.copyWith(color: NordicColors.primary),
        ),
        const SizedBox(height: 8.0),
        Text(
          'Báo cáo ca làm #${widget.assignmentId} đã được chuyển tới Admin phê duyệt bồi hoàn.',
          textAlign: TextAlign.center,
          style: NordicTypography.bodyRegular,
        ),
        const SizedBox(height: 24.0),

        NordicCard(
          child: Padding(
            padding: const EdgeInsets.all(16.0),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const Text(
                  'Chi tiết thanh toán bồi hoàn (BR-05):',
                  style: NordicTypography.labelMedium,
                ),
                const SizedBox(height: 12.0),
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    const Text('Thời gian chờ:', style: NordicTypography.bodySmall),
                    Text('${report.elapsedMinutes} phút', style: NordicTypography.labelMedium),
                  ],
                ),
                const SizedBox(height: 8.0),
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    Expanded(
                      child: Text('Cuộc gọi liên hệ:', style: NordicTypography.bodySmall),
                    ),
                    Text('${report.callAttempts} cuộc', style: NordicTypography.labelMedium),
                  ],
                ),
                const SizedBox(height: 8.0),
                const Divider(color: NordicColors.border, height: 1.0),
                const SizedBox(height: 8.0),
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    const Text('Tỷ lệ bồi hoàn thợ:', style: NordicTypography.bodySmall),
                    Text('${(report.workerFeeRate * 100).round()}%', style: NordicTypography.labelMedium),
                  ],
                ),
                const SizedBox(height: 8.0),
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    Expanded(
                      child: Text(
                        'Số tiền bồi hoàn thợ nhận:',
                        style: NordicTypography.labelMedium,
                      ),
                    ),
                    const SizedBox(width: 8.0),
                    Text(
                      _formatCurrency(report.absenceFeeAmount),
                      style: NordicTypography.h3.copyWith(color: NordicColors.primary),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ),
        const SizedBox(height: 24.0),

        NordicButton(
          key: const Key('finish_absent_screen_button'),
          label: 'Hoàn tất',
          onPressed: () {
            Navigator.of(context).maybePop();
          },
        ),
      ],
    );
  }
}
