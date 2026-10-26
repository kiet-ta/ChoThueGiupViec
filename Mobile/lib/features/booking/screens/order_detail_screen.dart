import 'dart:async';

import 'package:flutter/material.dart';

import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/avatar_initials.dart';
import '../../../core/widgets/bottom_action_bar.dart';
import '../../../core/widgets/grouped_list_card.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_text_input.dart';
import '../../../core/widgets/section_title.dart';
import '../../../core/widgets/status_pill.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../../../core/widgets/step_dots.dart';
import '../../disputes/models/dispute_models.dart';
import '../../disputes/routes.dart';
import '../../disputes/screens/dispute_screen.dart';
import '../../payments/models/payment_models.dart';
import '../../payments/routes.dart';
import '../../ratings/models/rating_models.dart';
import '../../ratings/routes.dart';
import '../../ratings/screens/rating_screen.dart';
import '../../workers/routes.dart';
import '../../workers/screens/customer_acceptance_screen.dart';
import '../logic/booking_logic.dart';
import '../logic/order_logic.dart';
import '../models/booking_models.dart';
import '../services/fallback_service.dart';
import '../services/order_service.dart';
import 'fallback_prompt_card.dart';
import 'order_status_pill.dart';

/// One order of the customer: its timeline and workers (MOB-M2-04), cancel, and "Làm lần 2" (MOB-M2-05).
/// Contract booking.md 3.5 to 3.8.
///
/// There is no realtime client in the project, so the screen asks the server again every few seconds (the contract's fallback)
/// while the order can still change.
class OrderDetailScreen extends StatefulWidget {
  /// The order to show; when null it is read from the route arguments.
  final OrderDetailArgs? args;
  final IOrderService? service;

  /// The BR-02 fallback question (an order with one worker and no second one); by default the real server.
  final IFallbackService? fallbackService;

  /// How often the order is asked again.
  final Duration pollInterval;

  /// What "Thanh toán" does; by default it opens the payment screen and reloads the order when the customer comes back.
  final void Function(PaymentScreenArgs args)? onPay;

  /// What the acceptance, rating and dispute buttons do (the route name and its arguments); by default it opens that screen
  /// and reloads the order when the customer comes back.
  final void Function(String route, Object args)? onOpen;

  const OrderDetailScreen({
    super.key,
    this.args,
    this.service,
    this.fallbackService,
    this.pollInterval = defaultProgressPollInterval,
    this.onPay,
    this.onOpen,
  });

  @override
  State<OrderDetailScreen> createState() => _OrderDetailScreenState();
}

class _OrderDetailScreenState extends State<OrderDetailScreen> {
  late final IOrderService _service;
  late final IFallbackService _fallbackService;
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
    _fallbackService = widget.fallbackService ?? FallbackService();
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_started) return;
    _started = true;
    final routeArgs = ModalRoute.of(context)?.settings.arguments;
    _orderId =
        widget.args?.orderId ??
        (routeArgs is OrderDetailArgs ? routeArgs.orderId : null);
    if (_orderId != null) _load();
  }

  @override
  void dispose() {
    _pollTimer?.cancel();
    _reason.dispose();
    super.dispose();
  }

  Future<(BookingOrder, OrderProgress)> _fetch(int orderId) async {
    final both = await Future.wait<Object>([
      _service.getOrder(orderId),
      _service.getProgress(orderId),
    ]);
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
    final args = PaymentScreenArgs(
      orderId: order.orderId,
      extensionId: extensionId,
      orderCode: order.orderCode,
    );
    if (widget.onPay != null) return widget.onPay!(args);
    Navigator.of(context)
        .pushNamed(PaymentRoutes.payment, arguments: args)
        .then((_) {
          if (mounted) _refresh();
        });
  }

  void _open(String route, Object args) {
    if (widget.onOpen != null) return widget.onOpen!(route, args);
    Navigator.of(context).pushNamed(route, arguments: args).then((_) {
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
      final cancelled = await _service.cancelOrder(
        order.orderId,
        _reason.text.trim(),
      );
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
    if (order == null ||
        progress == null ||
        _extSubmitting ||
        workers.isEmpty) {
      return;
    }
    final hours = _extHours;
    if (hours == null) {
      return setState(() => _extError = 'Chọn số giờ làm thêm.');
    }
    final assignmentId = workers.any((a) => a.assignmentId == _extAssignmentId)
        ? _extAssignmentId!
        : workers.first.assignmentId;

    _extSubmitting = true;
    setState(() => _extError = null);
    try {
      final extension = await _service.requestExtension(
        orderId: order.orderId,
        assignmentId: assignmentId,
        extraHours: hours,
      );
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
      appBar: AppBar(
        title: Text(
          order == null ? 'Đơn của bạn' : order.orderCode,
          key: const Key('order-title'),
        ),
      ),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: ListView(
            padding: const EdgeInsets.fromLTRB(20.0, 8.0, 20.0, 32.0),
            children: _body(),
          ),
        ),
      ),
      // ui-templates `.btn-bar`: paying is the one main action of an unpaid order.
      bottomNavigationBar: order != null && canPay(order.orderStatus)
          ? BottomActionBar(
              children: [
                NordicButton(
                  key: const Key('pay-order'),
                  label: 'Thanh toán ${formatVnd(order.totalAmount)}',
                  onPressed: _pay,
                ),
              ],
            )
          : null,
    );
  }

  List<Widget> _body() {
    if (_orderId == null) {
      return const [
        NordicCard(
          child: Text(
            'Màn hình này mở từ một đơn. Hãy chọn đơn trong lịch sử đơn.',
            style: NordicTypography.bodyRegular,
          ),
        ),
      ];
    }
    if (_loading && _order == null) {
      return const [LoadingStateWidget(message: 'Đang tải đơn…')];
    }
    if (_error != null) {
      return [
        ErrorStateWidget(
          title: 'Chưa tải được đơn',
          message: _error!,
          onRetry: _load,
        ),
      ];
    }

    final order = _order;
    final progress = _progress;
    if (order == null || progress == null) return const [];

    final workers = extendableAssignments(progress);
    final displayStatus = orderStatusForDisplay(
      order.orderStatus,
      progress.assignments,
    );
    final extension = progress.extension;
    final actions = _actions(order, progress);
    return [
      _summary(order, displayStatus),
      if (_notice != null) ...[
        const SizedBox(height: 12.0),
        Text(
          _notice!,
          key: const Key('order-notice'),
          style: NordicTypography.bodyRegular,
        ),
      ],
      if (order.orderStatus == OrderStatus.cancelled &&
          (order.cancelReason ?? '').isNotEmpty) ...[
        const SizedBox(height: 12.0),
        Text(
          'Lý do huỷ: ${order.cancelReason}',
          key: const Key('order-cancel-reason'),
          style: NordicTypography.bodySmall,
        ),
      ],
      if (order.orderStatus == OrderStatus.awaitingFallback) ...[
        const SizedBox(height: 16.0),
        FallbackPromptCard(
          key: ValueKey('fallback-${order.orderId}'),
          orderId: order.orderId,
          service: _fallbackService,
          onAnswered: _refresh,
        ),
      ],
      SectionTitle(
        'Thợ',
        trailing: '${progress.assignments.length}/${progress.requiredWorkers}',
      ),
      ..._assignments(order, progress),
      if (extension != null) ...[
        const SizedBox(height: 16.0),
        _extensionCard(extension),
      ],
      if (actions.isNotEmpty) ...[
        const SectionTitle('Việc tiếp theo'),
        GroupedListCard(items: actions),
      ],
      if (_extOpen &&
          canExtend(progress) &&
          order.orderStatus == OrderStatus.assigned) ...[
        const SizedBox(height: 16.0),
        _extensionForm(workers),
      ],
      if (canCancel(order.orderStatus)) ...[
        const SizedBox(height: 24.0),
        _cancelSection(),
      ],
    ];
  }

  /// `08:00` in Asia/Ho_Chi_Minh from the server's shift start, or null when the server sent none.
  static String? _localTime(DateTime? instant) {
    if (instant == null) return null;
    final t = instant.toUtc().add(const Duration(hours: 7));
    return '${t.hour.toString().padLeft(2, '0')}:${t.minute.toString().padLeft(2, '0')}';
  }

  Widget _summary(BookingOrder order, String displayStatus) {
    final start = _localTime(order.shiftStartAt);
    final end = _localTime(order.shiftEndAt);
    return NordicCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      start ?? shiftLabel(order.shiftCode),
                      style: NordicTypography.numberLarge,
                    ),
                    const SizedBox(height: 4.0),
                    Text(
                      scheduleText(order.scheduledDate, order.shiftCode),
                      key: const Key('order-schedule'),
                      style: NordicTypography.bodySmall,
                    ),
                    if (end != null)
                      Text('→ $end', style: NordicTypography.bodySmall),
                  ],
                ),
              ),
              Text(
                formatVnd(order.totalAmount),
                key: const Key('order-amount'),
                style: NordicTypography.numberSmall,
              ),
            ],
          ),
          const SizedBox(height: 12.0),
          Align(
            alignment: Alignment.centerLeft,
            child: OrderStatusPill(
              displayStatus,
              textKey: const Key('order-status'),
            ),
          ),
          const SizedBox(height: 8.0),
          Text(
            '${tierLabel(order.serviceTier)} · ${formatArea(order.areaSnapshotM2)} · ${order.requiredWorkers} thợ',
            style: NordicTypography.bodySmall,
          ),
          if ((order.customerNote ?? '').isNotEmpty) ...[
            const SizedBox(height: 4.0),
            Text(
              'Ghi chú: ${order.customerNote}',
              style: NordicTypography.bodySmall,
            ),
          ],
          const Divider(height: 32.0),
          _timeline(displayStatus),
        ],
      ),
    );
  }

  Widget _timeline(String status) {
    final steps = orderTimeline(status);
    return StepDots(
      key: const Key('order-timeline'),
      steps: [
        for (var i = 0; i < steps.length; i++)
          StepItem(
            steps[i].label,
            // "Có thợ" (and after) is a milestone already reached when it is the current step: show it ticked.
            // The waiting steps (pay, find a worker) stay an open ring while the order sits on them.
            steps[i].done || (steps[i].current && i >= 2)
                ? DotState.done
                : (steps[i].current ? DotState.current : DotState.todo),
          ),
      ],
    );
  }

  List<Widget> _assignments(BookingOrder order, OrderProgress progress) {
    if (progress.assignments.isEmpty) {
      return [
        Text(
          switch (order.orderStatus) {
            OrderStatus.pendingPayment =>
              'Thợ được tìm sau khi bạn thanh toán.',
            OrderStatus.cancelled => 'Đơn đã huỷ, không có thợ.',
            _ => 'Đang tìm thợ cho đơn của bạn. Màn hình tự cập nhật.',
          },
          key: const Key('no-assignments'),
          style: NordicTypography.bodySmall,
        ),
      ];
    }
    return [
      for (final a in progress.assignments) ...[
        NordicCard(
          key: Key('assignment-${a.assignmentId}'),
          child: Row(
            children: [
              AvatarInitials(workerName(a)),
              const SizedBox(width: 12.0),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(workerName(a), style: NordicTypography.titleSmall),
                    if (ratingText(a.workerRatingAvg).isNotEmpty)
                      Row(
                        children: [
                          const Icon(
                            Icons.star,
                            size: 16.0,
                            color: NordicColors.accentAmber,
                          ),
                          const SizedBox(width: 2.0),
                          Text(
                            ratingText(a.workerRatingAvg),
                            style: NordicTypography.bodySmall,
                          ),
                        ],
                      ),
                  ],
                ),
              ),
              const SizedBox(width: 8.0),
              // Full label width ("Chờ bạn nghiệm thu"); the name column gives way first.
              ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 180.0),
                child: StatusPill(
                  assignmentStatusLabel(a.assignmentStatus),
                  textKey: Key('assignment-status-${a.assignmentId}'),
                  dot: true,
                  tone: switch (a.assignmentStatus) {
                    AssignmentStatus.awaitingAcceptance => PillTone.warn,
                    AssignmentStatus.cancelledByWorker ||
                    AssignmentStatus.absent ||
                    AssignmentStatus.incident => PillTone.bad,
                    _ => PillTone.ok,
                  },
                ),
              ),
            ],
          ),
        ),
        const SizedBox(height: 8.0),
      ],
    ];
  }

  /// The rows of ui-templates "Khi thợ đang làm": what the customer can do now, each opening its screen.
  List<GroupedListItem> _actions(BookingOrder order, OrderProgress progress) {
    final named = progress.assignments.length > 1;
    return [
      if (canExtend(progress) &&
          order.orderStatus == OrderStatus.assigned &&
          !_extOpen)
        GroupedListItem(
          key: const Key('open-extension'),
          icon: Icons.more_time_outlined,
          title: 'Làm lần 2 (thêm giờ)',
          onTap: _openExtension,
        ),
      for (final a in progress.assignments)
        for (final action in assignmentActions(a.assignmentStatus))
          switch (action) {
            AssignmentAction.reviewAndAccept => GroupedListItem(
              key: Key('accept-${a.assignmentId}'),
              icon: Icons.photo_library_outlined,
              title: 'Xem ảnh & nghiệm thu',
              subtitle: named ? workerName(a) : null,
              onTap: () => _open(
                WorkerRoutes.customerAcceptance,
                CustomerAcceptanceArgs(assignmentId: a.assignmentId),
              ),
            ),
            AssignmentAction.rate => GroupedListItem(
              key: Key('rate-${a.assignmentId}'),
              icon: Icons.star_outline,
              title: 'Đánh giá thợ',
              subtitle: named ? workerName(a) : null,
              onTap: () => _open(
                RatingRoutes.ratings,
                RatingScreenArgs(
                  role: RatingRole.customer,
                  assignmentId: a.assignmentId,
                  ratedName: workerName(a),
                ),
              ),
            ),
          },
      if (canDispute(progress))
        GroupedListItem(
          key: const Key('dispute-order'),
          icon: Icons.flag_outlined,
          title: 'Khiếu nại về đơn này',
          onTap: () => _open(
            DisputeRoutes.disputes,
            DisputeScreenArgs(
              role: DisputeRole.customer,
              orderId: order.orderId,
            ),
          ),
        ),
    ];
  }

  Widget _extensionCard(OrderExtension extension) => NordicCard(
    key: const Key('extension-card'),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            const Icon(Icons.more_time_outlined, color: NordicColors.primary),
            const SizedBox(width: 8.0),
            const Expanded(
              child: Text('Làm lần 2', style: NordicTypography.titleSmall),
            ),
            ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 180.0),
              child: StatusPill(
                extensionStatusLabel(extension.extStatus),
                textKey: const Key('extension-status'),
                tone: switch (extension.extStatus) {
                  ExtensionStatus.accepted => PillTone.ok,
                  ExtensionStatus.pendingPayment ||
                  ExtensionStatus.paid => PillTone.warn,
                  _ => PillTone.bad,
                },
              ),
            ),
          ],
        ),
        const SizedBox(height: 8.0),
        Text(
          'Thêm ${hoursText(extension.extraHours)} · ${formatVnd(extension.extraAmount)}',
          key: const Key('extension-summary'),
          style: NordicTypography.titleSmall,
        ),
        if (extension.extStatus == ExtensionStatus.pendingPayment) ...[
          const SizedBox(height: 12.0),
          NordicButton(
            key: const Key('pay-extension'),
            label: 'Thanh toán ${formatVnd(extension.extraAmount)}',
            width: double.infinity,
            onPressed: () => _pay(extensionId: extension.extensionId),
          ),
        ],
      ],
    ),
  );

  Widget _extensionForm(List<ProgressAssignment> workers) {
    final options = _options;
    final selected = workers.any((a) => a.assignmentId == _extAssignmentId)
        ? _extAssignmentId
        : workers.first.assignmentId;
    return NordicCard(
      key: const Key('extension-form'),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text('Làm lần 2', style: NordicTypography.titleSmall),
          const SizedBox(height: 4.0),
          const Text(
            'Giá hiện sau khi gửi. Thợ được hỏi sau khi bạn thanh toán.',
            style: NordicTypography.bodySmall,
          ),
          if (workers.length > 1) ...[
            const SizedBox(height: 12.0),
            const Text('THỢ', style: NordicTypography.caps),
            const SizedBox(height: 6.0),
            Wrap(
              spacing: 8.0,
              runSpacing: 8.0,
              children: [
                for (final a in workers)
                  ChoiceChip(
                    key: Key('extension-worker-${a.assignmentId}'),
                    label: Text(workerName(a)),
                    selected: a.assignmentId == selected,
                    onSelected: (_) =>
                        setState(() => _extAssignmentId = a.assignmentId),
                  ),
              ],
            ),
          ],
          const SizedBox(height: 12.0),
          const Text('SỐ GIỜ THÊM', style: NordicTypography.caps),
          const SizedBox(height: 6.0),
          if (_extLoading) const LoadingStateWidget(message: 'Đang tải…'),
          if (options != null)
            Wrap(
              spacing: 8.0,
              runSpacing: 8.0,
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
            Text(
              _extError!,
              key: const Key('extension-error'),
              style: NordicTypography.bodySmall.copyWith(
                color: NordicColors.error,
              ),
            ),
          ],
          const SizedBox(height: 12.0),
          if (options != null)
            NordicButton(
              key: const Key('submit-extension'),
              label: 'Gửi yêu cầu',
              width: double.infinity,
              isLoading: _extSubmitting,
              onPressed: () => _requestExtension(workers),
            )
          else if (!_extLoading)
            NordicButton(
              label: 'Thử lại',
              width: double.infinity,
              variant: NordicButtonVariant.secondary,
              onPressed: _openExtension,
            ),
        ],
      ),
    );
  }

  Widget _cancelSection() {
    if (!_cancelOpen) {
      return SizedBox(
        width: double.infinity,
        child: OutlinedButton(
          key: const Key('open-cancel'),
          style: OutlinedButton.styleFrom(foregroundColor: NordicColors.error),
          onPressed: () => setState(() {
            _cancelOpen = true;
            _cancelError = null;
          }),
          child: const Text('Huỷ đơn'),
        ),
      );
    }
    return NordicCard(
      key: const Key('cancel-form'),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text('Huỷ đơn', style: NordicTypography.titleSmall),
          const SizedBox(height: 8.0),
          NordicTextInput(
            key: const Key('cancel-reason-input'),
            label: 'Lý do huỷ',
            controller: _reason,
            maxLines: 3,
          ),
          if (_cancelError != null) ...[
            const SizedBox(height: 8.0),
            Text(
              _cancelError!,
              key: const Key('cancel-error'),
              style: NordicTypography.bodySmall.copyWith(
                color: NordicColors.error,
              ),
            ),
          ],
          const SizedBox(height: 12.0),
          NordicButton(
            key: const Key('submit-cancel'),
            label: 'Xác nhận huỷ',
            width: double.infinity,
            isLoading: _cancelling,
            onPressed: _cancel,
          ),
          const SizedBox(height: 4.0),
          NordicButton(
            label: 'Không huỷ nữa',
            width: double.infinity,
            variant: NordicButtonVariant.ghost,
            onPressed: () => setState(() => _cancelOpen = false),
          ),
        ],
      ),
    );
  }
}
