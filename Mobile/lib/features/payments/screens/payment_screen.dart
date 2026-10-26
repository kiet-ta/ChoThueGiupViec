import 'dart:async';

import 'package:flutter/material.dart';

import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/progress_ring.dart';
import '../../../core/widgets/status_pill.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../logic/payment_logic.dart';
import '../models/payment_models.dart';
import '../services/payments_service.dart';

/// QR payment of an order or of an extension (MOB-M2-03, MOB-M2-05); contract payments.md 2.1 to 2.3.
///
/// Sandbox only: the banner "SANDBOX – no real money" is always on screen (decision Q04).
/// Two limits of the project today, both said on screen: there is no QR-rendering package, so the QR content is shown as text;
/// and there is no realtime client, so "paid" arrives by asking the server every few seconds (the contract's fallback).
class PaymentScreen extends StatefulWidget {
  /// What to pay; when null it is read from the route arguments.
  final PaymentScreenArgs? args;
  final IPaymentsService? service;

  /// The clock, so the countdown and the deadline can be tested; the real screen uses the device time.
  final DateTime Function()? now;

  /// How often the status is asked again.
  final Duration pollInterval;

  /// What "Xem đơn" does after the payment; by default the screen closes and returns the paid [Payment].
  final void Function(Payment payment)? onPaid;

  const PaymentScreen({
    super.key,
    this.args,
    this.service,
    this.now,
    this.pollInterval = defaultPollInterval,
    this.onPaid,
  });

  @override
  State<PaymentScreen> createState() => _PaymentScreenState();
}

class _PaymentScreenState extends State<PaymentScreen>
    with WidgetsBindingObserver {
  late final IPaymentsService _service;
  PaymentScreenArgs? _args;
  bool _started = false;

  Payment? _payment;
  String? _error;
  bool _loading = false;
  bool _creating = false; // one request per tap
  bool _polling = false; // one status check at a time
  bool _returnedForPaidOrder = false;
  int _pollFailures = 0;

  Timer? _pollTimer;
  Timer? _tickTimer;

  DateTime _now() => (widget.now ?? DateTime.now)();

  @override
  void initState() {
    super.initState();
    _service = widget.service ?? PaymentsService();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_started) return;
    _started = true;
    final routeArgs = ModalRoute.of(context)?.settings.arguments;
    _args = widget.args ?? (routeArgs is PaymentScreenArgs ? routeArgs : null);
    if (_args != null) _createQr();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _stopTimers();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      unawaited(_refreshAfterResume());
    }
  }

  Future<void> _refreshAfterResume() async {
    final payment = _payment;
    if (payment == null) {
      if (mounted) setState(() {});
      return;
    }
    _stopTimers();
    await _poll();
    if (!mounted) return;
    _watch();
  }

  void _stopTimers() {
    _pollTimer?.cancel();
    _tickTimer?.cancel();
    _pollTimer = null;
    _tickTimer = null;
  }

  Future<void> _createQr() async {
    final args = _args;
    if (args == null || _creating) return;
    _creating = true;
    _stopTimers();
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final extensionId = args.extensionId;
      final payment = extensionId != null
          ? await _service.createExtensionQr(extensionId)
          : await _service.createOrderQr(args.orderId);
      if (!mounted) return;
      setState(() {
        _payment = payment;
        _loading = false;
        _pollFailures = 0;
      });
      _watch();
      _returnToPaidOrder(payment);
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _payment = null;
        _error = qrFailureMessage(e);
        _loading = false;
      });
    } finally {
      _creating = false;
    }
  }

  /// Starts the countdown and the status polling while the payment can still change.
  void _watch() {
    final payment = _payment;
    if (payment == null || !shouldKeepPolling(payment, _now())) return;
    _stopTimers();
    _tickTimer = Timer.periodic(const Duration(seconds: 1), (_) {
      if (!mounted) return;
      final current = _payment;
      if (current == null || !shouldKeepPolling(current, _now())) _stopTimers();
      setState(
        () {},
      ); // the countdown, or the switch to "expired" when the deadline passes
    });
    _pollTimer = Timer.periodic(widget.pollInterval, (_) => _poll());
  }

  Future<void> _poll() async {
    final payment = _payment;
    if (payment == null || _polling) return;
    _polling = true;
    try {
      final fresh = await _service.getPayment(payment.paymentId);
      if (!mounted) return;
      setState(() {
        // The pay URL is only returned when the QR is created; keep the one we have.
        _payment = fresh.payUrl == null && payment.payUrl != null
            ? _withPayUrl(fresh, payment.payUrl)
            : fresh;
        _pollFailures = 0;
      });
      if (!shouldKeepPolling(fresh, _now())) _stopTimers();
      _returnToPaidOrder(fresh);
    } on ApiException {
      if (!mounted) return;
      setState(() => _pollFailures++);
    } finally {
      _polling = false;
    }
  }

  static Payment _withPayUrl(Payment p, String? payUrl) => Payment(
    paymentId: p.paymentId,
    purpose: p.purpose,
    orderId: p.orderId,
    extensionId: p.extensionId,
    gateway: p.gateway,
    amount: p.amount,
    txnStatus: p.txnStatus,
    qrPayload: p.qrPayload,
    payUrl: payUrl,
    expiresAt: p.expiresAt,
    paidAt: p.paidAt,
    createdAt: p.createdAt,
    sandbox: p.sandbox,
  );

  void _done(Payment payment) {
    if (widget.onPaid != null) return widget.onPaid!(payment);
    Navigator.of(context).maybePop(payment);
  }

  void _returnToPaidOrder(Payment payment) {
    if (_returnedForPaidOrder ||
        payment.purpose != 'ORDER' ||
        payment.extensionId != null ||
        !payment.isPaid) {
      return;
    }
    _returnedForPaidOrder = true;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) _done(payment);
    });
  }

  @override
  Widget build(BuildContext context) {
    final args = _args;
    return Scaffold(
      appBar: AppBar(title: const Text('Thanh toán')),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: ListView(
            padding: const EdgeInsets.fromLTRB(20.0, 8.0, 20.0, 32.0),
            children: [
              const _SandboxBanner(),
              if (args?.orderCode != null) ...[
                const SizedBox(height: 24.0),
                Text(
                  'Đơn ${args!.orderCode}',
                  textAlign: TextAlign.center,
                  style: NordicTypography.bodySmall,
                ),
              ],
              const SizedBox(height: 16.0),
              ..._body(args),
            ],
          ),
        ),
      ),
    );
  }

  List<Widget> _body(PaymentScreenArgs? args) {
    if (args == null) {
      return const [
        NordicCard(
          child: Text(
            'Màn hình này mở từ một đơn cần thanh toán. Hãy đặt đơn hoặc mở đơn đang chờ thanh toán.',
            style: NordicTypography.bodyRegular,
          ),
        ),
      ];
    }
    if (_loading && _payment == null) {
      return const [LoadingStateWidget(message: 'Đang tạo mã thanh toán…')];
    }
    if (_error != null) {
      return [
        ErrorStateWidget(
          title: 'Chưa tạo được mã thanh toán',
          message: _error!,
          onRetry: _createQr,
        ),
      ];
    }

    final payment = _payment;
    if (payment == null) return const [];

    final now = _now();
    return switch (phaseOf(payment, now)) {
      PaymentPhase.waiting => _waiting(payment, now),
      PaymentPhase.paid => _paid(payment),
      PaymentPhase.expired => _expired(),
      PaymentPhase.refunded => _refunded(payment),
    };
  }

  /// Share of the QR's life still left (1 = just created), for the bar under the stats.
  double _lifeLeft(Payment payment, DateTime now) {
    final start = payment.createdAt;
    final end = payment.expiresAt;
    if (start == null || end == null) return 0.0;
    final total = end.difference(start).inSeconds;
    if (total <= 0) return 0.0;
    return (timeLeft(payment, now).inSeconds / total).clamp(0.0, 1.0);
  }

  List<Widget> _waiting(Payment payment, DateTime now) {
    final amount = formatVnd(payment.amount);
    return [
      Text(
        purposeLabel(payment),
        textAlign: TextAlign.center,
        style: NordicTypography.bodySmall,
      ),
      const SizedBox(height: 8.0),
      Text(
        amount,
        key: const Key('payment-amount'),
        textAlign: TextAlign.center,
        style: NordicTypography.numberLarge.copyWith(fontSize: 44.0),
      ),
      const SizedBox(height: 24.0),
      NordicCard(
        padding: const EdgeInsets.all(20.0),
        child: Column(
          children: [
            const Row(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Icon(
                  Icons.qr_code_scanner_outlined,
                  size: 18.0,
                  color: NordicColors.textSecondary,
                ),
                SizedBox(width: 6.0),
                Text('Nội dung mã QR', style: NordicTypography.bodySmall),
              ],
            ),
            const SizedBox(height: 12.0),
            Container(
              width: double.infinity,
              padding: const EdgeInsets.all(12.0),
              decoration: BoxDecoration(
                color: NordicColors.surfaceLow,
                borderRadius: BorderRadius.circular(12.0),
              ),
              child: SelectableText(
                payment.qrPayload ?? '—',
                key: const Key('payment-qr-payload'),
                style: NordicTypography.bodyRegular,
              ),
            ),
            if (payment.payUrl != null) ...[
              const SizedBox(height: 12.0),
              const Text(
                'Trang thanh toán sandbox',
                style: NordicTypography.caps,
              ),
              const SizedBox(height: 4.0),
              SelectableText(
                payment.payUrl!,
                key: const Key('payment-pay-url'),
                textAlign: TextAlign.center,
                style: NordicTypography.bodySmall,
              ),
            ],
            const SizedBox(height: 12.0),
            const Text(
              'Ứng dụng chưa vẽ được mã QR dạng hình: sao chép nội dung trên để thanh toán thử.',
              textAlign: TextAlign.center,
              style: NordicTypography.bodySmall,
            ),
          ],
        ),
      ),
      const SizedBox(height: 16.0),
      Row(
        children: [
          Expanded(
            child: _statBox(
              icon: Icons.timer_outlined,
              label: 'Hết hạn sau',
              child: Text(
                countdownText(timeLeft(payment, now)),
                key: const Key('payment-countdown'),
                style: NordicTypography.numberMedium,
              ),
            ),
          ),
          const SizedBox(width: 12.0),
          Expanded(
            child: _statBox(
              icon: Icons.sync,
              label: 'Trạng thái',
              child: const StatusPill(
                'Đang chờ',
                tone: PillTone.warn,
                dot: true,
              ),
            ),
          ),
        ],
      ),
      const SizedBox(height: 16.0),
      ClipRRect(
        borderRadius: BorderRadius.circular(3.0),
        child: LinearProgressIndicator(
          value: _lifeLeft(payment, now),
          minHeight: 6.0,
          backgroundColor: NordicColors.surfaceContainer,
          color: NordicColors.primary,
        ),
      ),
      const SizedBox(height: 12.0),
      Text(
        _pollFailures >= 3
            ? pollTroubleMessage
            : 'Đang chờ thanh toán. Màn hình tự cập nhật khi tiền về.',
        key: const Key('payment-waiting-note'),
        style: NordicTypography.bodySmall,
      ),
    ];
  }

  Widget _statBox({
    required IconData icon,
    required String label,
    required Widget child,
  }) => Container(
    height: 96.0,
    padding: const EdgeInsets.all(14.0),
    decoration: BoxDecoration(
      color: NordicColors.surface,
      borderRadius: BorderRadius.circular(16.0),
      border: Border.all(color: NordicColors.border),
    ),
    child: Column(
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        Row(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(icon, size: 16.0, color: NordicColors.textSecondary),
            const SizedBox(width: 6.0),
            Flexible(
              child: Text(
                label,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: NordicTypography.bodySmall,
              ),
            ),
          ],
        ),
        const SizedBox(height: 8.0),
        child,
      ],
    ),
  );

  List<Widget> _paid(Payment payment) => [
    const SizedBox(height: 16.0),
    const Center(
      child: ProgressRing(
        value: 1.0,
        size: 120.0,
        child: Icon(Icons.check, size: 48.0, color: NordicColors.primary),
      ),
    ),
    const SizedBox(height: 16.0),
    const Text(
      'Đã thanh toán',
      key: Key('payment-paid'),
      textAlign: TextAlign.center,
      style: NordicTypography.titleMedium,
    ),
    const SizedBox(height: 4.0),
    Text(
      'Đã nhận ${formatVnd(payment.amount)}.',
      textAlign: TextAlign.center,
      style: NordicTypography.bodyRegular,
    ),
    const SizedBox(height: 24.0),
    NordicButton(
      label: 'Xem đơn',
      width: double.infinity,
      onPressed: () => _done(payment),
    ),
  ];

  List<Widget> _expired() => [
    const NordicCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(Icons.timer_outlined, color: NordicColors.error),
              SizedBox(width: 8.0),
              Expanded(
                child: Text(
                  'Đã hết hạn thanh toán',
                  key: Key('payment-expired'),
                  style: NordicTypography.titleSmall,
                ),
              ),
            ],
          ),
          SizedBox(height: 8.0),
          Text(
            'Mã thanh toán không còn hiệu lực. Bạn không bị trừ tiền; đơn chưa thanh toán sẽ được huỷ.',
            style: NordicTypography.bodySmall,
          ),
        ],
      ),
    ),
    const SizedBox(height: 16.0),
    NordicButton(
      label: 'Quay lại',
      width: double.infinity,
      variant: NordicButtonVariant.secondary,
      onPressed: () => Navigator.of(context).maybePop(),
    ),
  ];

  List<Widget> _refunded(Payment payment) => [
    NordicCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text(
            'Đã hoàn tiền',
            key: Key('payment-refunded'),
            style: NordicTypography.titleSmall,
          ),
          const SizedBox(height: 8.0),
          Text(
            'Khoản ${formatVnd(payment.amount)} đã được hoàn lại.',
            style: NordicTypography.bodyRegular,
          ),
        ],
      ),
    ),
  ];
}

/// Decision Q04: every payment screen says, permanently, that no real money moves (ui-templates `.sandbox`).
class _SandboxBanner extends StatelessWidget {
  const _SandboxBanner();

  @override
  Widget build(BuildContext context) {
    return Container(
      key: const Key('sandbox-banner'),
      width: double.infinity,
      padding: const EdgeInsets.symmetric(horizontal: 14.0, vertical: 10.0),
      decoration: BoxDecoration(
        color: NordicColors.amberSoft,
        borderRadius: BorderRadius.circular(12.0),
      ),
      child: const Row(
        children: [
          Icon(
            Icons.science_outlined,
            color: NordicColors.onAmberSoft,
            size: 18.0,
          ),
          SizedBox(width: 8.0),
          Expanded(
            child: Text(
              sandboxBannerText,
              style: TextStyle(
                fontSize: 12.0,
                fontWeight: FontWeight.w600,
                color: NordicColors.onAmberSoft,
              ),
            ),
          ),
        ],
      ),
    );
  }
}
