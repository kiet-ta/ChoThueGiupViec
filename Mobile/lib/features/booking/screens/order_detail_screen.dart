import 'dart:async';

import 'package:flutter/material.dart';
import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_text_input.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../../payments/models/payment_models.dart';
import '../../payments/routes.dart';
import '../logic/booking_logic.dart';
import '../logic/order_logic.dart';
import '../models/booking_models.dart';
import '../services/order_service.dart';

/// One order of the customer: its timeline and workers (MOB-M2-04), cancel, and "Làm lần 2" (MOB-M2-05).
/// Contract booking.md 3.5 to 3.8.
///
/// There is no realtime client in the project, so the screen asks the server again every few seconds (the contract's fallback)
/// while the order can still change.
class OrderDetailScreen extends StatefulWidget {
  /// The order to show; when null it is read from the route arguments.
  final OrderDetailArgs? args;
  final IOrderService? service;

  /// How often the order is asked again.
  final Duration pollInterval;

  /// What "Thanh toán" does; by default it opens the payment screen and reloads the order when the customer comes back.
  final void Function(PaymentScreenArgs args)? onPay;

  const OrderDetailScreen({super.key, this.args, this.service, this.pollInterval = defaultProgressPollInterval, this.onPay});

  @override
  State<OrderDetailScreen> createState() => _OrderDetailScreenState();
}

class _OrderDetailScreenState extends State<OrderDetailScreen> {
  late final IOrderService _service;
  int? _orderId;
  bool _started = false;

  BookingOrder? _order;
  OrderProgress? _progress;
  String? _error;
  bool _loading = false;
  bool _refreshing = false; // one refresh at a time
  Timer? _pollTimer;

  // Cancel
  final _reason = TextEditingController();
  bool _cancelOpen = false;
  bool _cancelling = false; // one request per tap
  String? _cancelError;
  String? _notice;

  // "Làm lần 2"
  bool _extOpen = false;
  bool _extLoading = false;
  bool _extSubmitting = false; // one request per tap
  BookingOptions? _options;
  int? _extAssignmentId;
  double? _extHours;
  String? _extError;

  @override
  void initState() {
    super.initState();
    _service = widget.service ?? OrderService();
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_started) return;
    _started = true;
    final routeArgs = ModalRoute.of(context)?.settings.arguments;
    _orderId = widget.args?.orderId ?? (routeArgs is OrderDetailArgs ? routeArgs.orderId : null);
    if (_orderId != null) _load();
  }

  @override
  void dispose() {
    _pollTimer?.cancel();
    _reason.dispose();
    super.dispose();
  }

  Future<(BookingOrder, OrderProgress)> _fetch(int orderId) async {
    final both = await Future.wait<Object>([_service.getOrder(orderId), _service.getProgress(orderId)]);
    return (both[0] as BookingOrder, both[1] as OrderProgress);
  }

  Future<void> _load() async {
    final orderId = _orderId;
    if (orderId == null) return;
    _pollTimer?.cancel();
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final (order, progress) = await _fetch(orderId);
      if (!mounted) return;
      setState(() {
        _order = order;
        _progress = progress;
        _loading = false;
      });
      _watch();
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _error = orderLoadFailure(e);
        _loading = false;
      });
    }
  }

  /// Keeps asking while the order can still change.
  void _watch() {
    _pollTimer?.cancel();
    _pollTimer = null;
    final order = _order;
    if (order == null || !shouldPollOrder(order.orderStatus)) return;
    _pollTimer = Timer.periodic(widget.pollInterval, (_) => _refresh());
  }

  /// A quiet reload: a failure keeps what is on screen and the next tick tries again.
  Future<void> _refresh() async {
    final orderId = _orderId;
    if (orderId == null || _refreshing || _cancelling || _extSubmitting) return;
    _refreshing = true;
    try {
      final (order, progress) = await _fetch(orderId);
      if (!mounted) return;
      setState(() {
        _order = order;
        _progress = progress;
      });
      if (!shouldPollOrder(order.orderStatus)) {
        _pollTimer?.cancel();
        _pollTimer = null;
      }
    } on ApiException {
      // Kept quiet on purpose, see above.
    } finally {
      _refreshing = false;
    }
  }

  void _pay({int? extensionId}) {
    final order = _order;
    if (order == null) return;
    final args = PaymentScreenArgs(orderId: order.orderId, extensionId: extensionId, orderCode: order.orderCode);
    if (widget.onPay != null) return widget.onPay!(args);
    Navigator.of(context).pushNamed(PaymentRoutes.payment, arguments: args).then((_) {
      if (mounted) _refresh();
    });
  }

  Future<void> _cancel() async {
    final order = _order;
    if (order == null || _cancelling) return;
    final problem = validateCancelReason(_reason.text);
    if (problem != null) return setState(() => _cancelError = problem);

    _cancelling = true;
    setState(() => _cancelError = null);
    try {
      final cancelled = await _service.cancelOrder(order.orderId, _reason.text.trim());
      if (!mounted) return;
      setState(() {
        _order = cancelled;
        _cancelOpen = false;
        _extOpen = false;
        _notice = 'Đã huỷ đơn. Khoản đã thanh toán (nếu có) sẽ được hoàn lại.';
      });
      _watch();
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() => _cancelError = cancelFailure(e));
    } finally {
      _cancelling = false;
      if (mounted) setState(() {});
    }
  }

  Future<void> _openExtension() async {
    setState(() {
      _extOpen = true;
      _extError = null;
    });
    if (_options != null || _extLoading) return;
    setState(() => _extLoading = true);
    try {
      final options = await _service.getOptions();
      if (!mounted) return;
      setState(() {
        _options = options;
        _extLoading = false;
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _extError = orderLoadFailure(e);
        _extLoading = false;
      });
    }
  }

  Future<void> _requestExtension(List<ProgressAssignment> workers) async {
    final order = _order;
    final progress = _progress;
    if (order == null || progress == null || _extSubmitting || workers.isEmpty) return;
    final hours = _extHours;
    if (hours == null) return setState(() => _extError = 'Chọn số giờ làm thêm.');
    final assignmentId = workers.any((a) => a.assignmentId == _extAssignmentId) ? _extAssignmentId! : workers.first.assignmentId;

    _extSubmitting = true;
    setState(() => _extError = null);
    try {
      final extension = await _service.requestExtension(orderId: order.orderId, assignmentId: assignmentId, extraHours: hours);
      if (!mounted) return;
      setState(() {
        _progress = OrderProgress(
          orderId: progress.orderId,
          orderStatus: progress.orderStatus,
          requiredWorkers: progress.requiredWorkers,
          assignments: progress.assignments,
          extension: extension,
        );
        _extOpen = false;
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() => _extError = extensionFailure(e));
    } finally {
      _extSubmitting = false;
      if (mounted) setState(() {});
    }
  }

  @override
  Widget build(BuildContext context) {
    final order = _order;
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(title: Text(order == null ? 'Đơn của bạn' : 'Đơn ${order.orderCode}')),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: ListView(
            padding: const EdgeInsets.symmetric(horizontal: 16.0, vertical: 16.0),
            children: _body(),
          ),
        ),
      ),
    );
  }

  List<Widget> _body() {
    if (_orderId == null) {
      return const [
        NordicCard(child: Text('Màn hình này mở từ một đơn. Hãy chọn đơn trong lịch sử đơn.', style: NordicTypography.bodyRegular)),
      ];
    }
    if (_loading && _order == null) return const [LoadingStateWidget(message: 'Đang tải đơn…')];
    if (_error != null) return [ErrorStateWidget(title: 'Chưa tải được đơn', message: _error!, onRetry: _load)];

    final order = _order;
    final progress = _progress;
    if (order == null || progress == null) return const [];

    final workers = extendableAssignments(progress);
    final extension = progress.extension;
    return [
      _summary(order),
      const SizedBox(height: 16.0),
      _timeline(order.orderStatus),
      if (_notice != null) ...[
        const SizedBox(height: 12.0),
        Text(_notice!, key: const Key('order-notice'), style: NordicTypography.bodyRegular),
      ],
      if (order.orderStatus == OrderStatus.cancelled && (order.cancelReason ?? '').isNotEmpty) ...[
        const SizedBox(height: 12.0),
        Text('Lý do huỷ: ${order.cancelReason}', key: const Key('order-cancel-reason'), style: NordicTypography.bodySmall),
      ],
      if (canPay(order.orderStatus)) ...[
        const SizedBox(height: 16.0),
        NordicButton(key: const Key('pay-order'), label: 'Thanh toán ${formatVnd(order.totalAmount)}', onPressed: _pay),
      ],
      const SizedBox(height: 24.0),
      const Text('Thợ của đơn', style: NordicTypography.h3),
      const SizedBox(height: 8.0),
      ..._assignments(order, progress),
      if (extension != null) ...[
        const SizedBox(height: 24.0),
        _extensionCard(extension),
      ],
      if (canExtend(progress) && order.orderStatus == OrderStatus.assigned) ...[
        const SizedBox(height: 24.0),
        _extensionForm(workers),
      ],
      if (canCancel(order.orderStatus)) ...[
        const SizedBox(height: 24.0),
        _cancelSection(),
      ],
    ];
  }

  Widget _summary(BookingOrder order) => NordicCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(orderStatusLabel(order.orderStatus), key: const Key('order-status'), style: NordicTypography.h2),
            const SizedBox(height: 8.0),
            Text(scheduleText(order.scheduledDate, order.shiftCode), key: const Key('order-schedule'), style: NordicTypography.bodyRegular),
            const SizedBox(height: 4.0),
            Text(
              '${tierLabel(order.serviceTier)} · ${formatArea(order.areaSnapshotM2)} · ${order.requiredWorkers} thợ',
              style: NordicTypography.bodySmall,
            ),
            const SizedBox(height: 8.0),
            Text(formatVnd(order.totalAmount), key: const Key('order-amount'), style: NordicTypography.priceHighlight),
            if ((order.customerNote ?? '').isNotEmpty) ...[
              const SizedBox(height: 8.0),
              Text('Ghi chú: ${order.customerNote}', style: NordicTypography.bodySmall),
            ],
          ],
        ),
      );

  Widget _timeline(String status) {
    final steps = orderTimeline(status);
    return Row(
      key: const Key('order-timeline'),
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        for (final step in steps)
          Expanded(
            child: Column(
              children: [
                Icon(
                  step.done ? Icons.check_circle_rounded : (step.current ? Icons.radio_button_checked : Icons.radio_button_unchecked),
                  color: step.done || step.current ? NordicColors.primary : NordicColors.border,
                  size: 22.0,
                ),
                const SizedBox(height: 4.0),
                Text(
                  step.label,
                  textAlign: TextAlign.center,
                  style: NordicTypography.labelSmall.copyWith(
                    color: step.done || step.current ? NordicColors.textTitle : NordicColors.textSecondary,
                    fontWeight: step.current ? FontWeight.w700 : null,
                  ),
                ),
              ],
            ),
          ),
      ],
    );
  }

  List<Widget> _assignments(BookingOrder order, OrderProgress progress) {
    if (progress.assignments.isEmpty) {
      return [
        Text(
          switch (order.orderStatus) {
            OrderStatus.pendingPayment => 'Thợ được tìm sau khi bạn thanh toán.',
            OrderStatus.cancelled => 'Đơn đã huỷ, không có thợ.',
            _ => 'Đang tìm thợ cho đơn của bạn. Màn hình tự cập nhật.',
          },
          key: const Key('no-assignments'),
          style: NordicTypography.bodyRegular,
        ),
      ];
    }
    return [
      for (final a in progress.assignments) ...[
        NordicCard(
          key: Key('assignment-${a.assignmentId}'),
          child: Row(
            children: [
              const Icon(Icons.person_outline_rounded, color: NordicColors.primary),
              const SizedBox(width: 12.0),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      ratingText(a.workerRatingAvg).isEmpty ? workerName(a) : '${workerName(a)} · ★ ${ratingText(a.workerRatingAvg)}',
                      style: NordicTypography.labelMedium,
                    ),
                    const SizedBox(height: 2.0),
                    Text(assignmentStatusLabel(a.assignmentStatus), key: Key('assignment-status-${a.assignmentId}'), style: NordicTypography.bodySmall),
                  ],
                ),
              ),
            ],
          ),
        ),
        const SizedBox(height: 8.0),
      ],
    ];
  }

  Widget _extensionCard(OrderExtension extension) => NordicCard(
        key: const Key('extension-card'),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text('Làm lần 2', style: NordicTypography.h3),
            const SizedBox(height: 4.0),
            Text(
              'Thêm ${hoursText(extension.extraHours)} · ${formatVnd(extension.extraAmount)}',
              key: const Key('extension-summary'),
              style: NordicTypography.bodyRegular,
            ),
            const SizedBox(height: 4.0),
            Text(extensionStatusLabel(extension.extStatus), key: const Key('extension-status'), style: NordicTypography.bodySmall),
            if (extension.extStatus == ExtensionStatus.pendingPayment) ...[
              const SizedBox(height: 12.0),
              NordicButton(
                key: const Key('pay-extension'),
                label: 'Thanh toán ${formatVnd(extension.extraAmount)}',
                onPressed: () => _pay(extensionId: extension.extensionId),
              ),
            ],
          ],
        ),
      );

  Widget _extensionForm(List<ProgressAssignment> workers) {
    if (!_extOpen) {
      return NordicButton(
        key: const Key('open-extension'),
        label: 'Làm lần 2 (thêm giờ)',
        variant: NordicButtonVariant.secondary,
        onPressed: _openExtension,
      );
    }
    final options = _options;
    final selected = workers.any((a) => a.assignmentId == _extAssignmentId) ? _extAssignmentId : workers.first.assignmentId;
    return NordicCard(
      key: const Key('extension-form'),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text('Làm lần 2', style: NordicTypography.h3),
          const SizedBox(height: 4.0),
          const Text(
            'Nhờ thợ ở lại làm thêm. Giá do hệ thống tính và hiện ra sau khi gửi yêu cầu; thợ chỉ được hỏi sau khi bạn thanh toán.',
            style: NordicTypography.bodySmall,
          ),
          if (workers.length > 1) ...[
            const SizedBox(height: 12.0),
            const Text('Thợ', style: NordicTypography.labelMedium),
            Wrap(
              spacing: 8.0,
              children: [
                for (final a in workers)
                  ChoiceChip(
                    key: Key('extension-worker-${a.assignmentId}'),
                    label: Text(workerName(a)),
                    selected: a.assignmentId == selected,
                    onSelected: (_) => setState(() => _extAssignmentId = a.assignmentId),
                  ),
              ],
            ),
          ],
          const SizedBox(height: 12.0),
          const Text('Số giờ làm thêm', style: NordicTypography.labelMedium),
          if (_extLoading) const LoadingStateWidget(message: 'Đang tải…'),
          if (options != null)
            Wrap(
              spacing: 8.0,
              children: [
                for (final h in extraHourOptions(options.shiftMaxHours))
                  ChoiceChip(
                    key: Key('hours-$h'),
                    label: Text(hoursText(h)),
                    selected: _extHours == h,
                    onSelected: (_) => setState(() => _extHours = h),
                  ),
              ],
            ),
          if (_extError != null) ...[
            const SizedBox(height: 8.0),
            Text(_extError!, key: const Key('extension-error'), style: NordicTypography.bodySmall.copyWith(color: NordicColors.error)),
          ],
          const SizedBox(height: 12.0),
          if (options != null)
            NordicButton(
              key: const Key('submit-extension'),
              label: 'Gửi yêu cầu',
              isLoading: _extSubmitting,
              onPressed: () => _requestExtension(workers),
            )
          else if (!_extLoading)
            NordicButton(label: 'Thử lại', variant: NordicButtonVariant.secondary, onPressed: _openExtension),
        ],
      ),
    );
  }

  Widget _cancelSection() {
    if (!_cancelOpen) {
      return NordicButton(
        key: const Key('open-cancel'),
        label: 'Huỷ đơn',
        variant: NordicButtonVariant.ghost,
        onPressed: () => setState(() {
          _cancelOpen = true;
          _cancelError = null;
        }),
      );
    }
    return NordicCard(
      key: const Key('cancel-form'),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text('Huỷ đơn', style: NordicTypography.h3),
          const SizedBox(height: 8.0),
          NordicTextInput(
            key: const Key('cancel-reason-input'),
            label: 'Lý do huỷ',
            controller: _reason,
            maxLines: 3,
          ),
          if (_cancelError != null) ...[
            const SizedBox(height: 8.0),
            Text(_cancelError!, key: const Key('cancel-error'), style: NordicTypography.bodySmall.copyWith(color: NordicColors.error)),
          ],
          const SizedBox(height: 12.0),
          NordicButton(key: const Key('submit-cancel'), label: 'Xác nhận huỷ', isLoading: _cancelling, onPressed: _cancel),
          NordicButton(
            label: 'Không huỷ nữa',
            variant: NordicButtonVariant.ghost,
            onPressed: () => setState(() => _cancelOpen = false),
          ),
        ],
      ),
    );
  }
}
