import 'package:flutter/material.dart';

import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_elevation.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/pressable.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../logic/booking_logic.dart';
import '../logic/order_logic.dart';
import '../models/booking_models.dart';
import '../routes.dart';
import '../services/order_service.dart';
import 'order_status_pill.dart';

/// The customer's orders (MOB-M2-06, customer redesign v2 §4.1); contract booking.md 3.4.
/// Filter chips ask the server for one status; the open orders that have not started are shown first as
/// dark cards ("Sắp tới"); every order is listed once under its month, whatever order the server sent them in.
class OrderHistoryScreen extends StatefulWidget {
  final IOrderService? service;

  /// What a tap on an order does; by default it opens the order screen and reloads the list on return.
  final void Function(OrderSummary order)? onOpenOrder;

  /// What "Đặt đơn" does when there is no order yet; by default it opens the booking screen.
  final VoidCallback? onBook;

  /// The current time, for a test; by default the device clock.
  final DateTime Function()? clock;

  const OrderHistoryScreen({
    super.key,
    this.service,
    this.onOpenOrder,
    this.onBook,
    this.clock,
  });

  @override
  State<OrderHistoryScreen> createState() => _OrderHistoryScreenState();
}

class _OrderHistoryScreenState extends State<OrderHistoryScreen> {
  late final IOrderService _service;
  final _items = <OrderSummary>[];
  BookingOptions? _options;

  /// The status the server is asked for; null lists every status.
  String? _filter;
  int _total = 0;
  int _page = 0;
  bool _loading = true;
  bool _loadingMore = false; // one request per tap
  String? _error;
  String? _moreError;

  @override
  void initState() {
    super.initState();
    _service = widget.service ?? OrderService();
    _load();
  }

  Future<void> _load() async {
    final filter = _filter;
    setState(() {
      _loading = true;
      _error = null;
      _moreError = null;
    });
    try {
      final page = await _service.listOrders(
        page: 1,
        pageSize: historyPageSize,
        status: filter,
      );
      // The shift times come from Booking; the list still works without them.
      BookingOptions? options = _options;
      if (options == null) {
        try {
          options = await _service.getOptions();
        } catch (_) {}
      }
      if (!mounted || filter != _filter) return;
      setState(() {
        _options = options;
        _items
          ..clear()
          ..addAll(page.items);
        _total = page.total;
        _page = 1;
        _loading = false;
      });
    } on ApiException catch (e) {
      if (!mounted || filter != _filter) return;
      setState(() {
        _error = loadFailureMessage(e);
        _loading = false;
      });
    }
  }

  void _setFilter(String? filter) {
    if (filter == _filter) return;
    _filter = filter;
    _load();
  }

  Future<void> _more() async {
    if (_loadingMore) return;
    _loadingMore = true;
    setState(() => _moreError = null);
    final filter = _filter;
    try {
      final page = await _service.listOrders(
        page: _page + 1,
        pageSize: historyPageSize,
        status: filter,
      );
      if (!mounted || filter != _filter) return;
      setState(() {
        // A new order pushes the pages down by one; never show the same order twice.
        final known = {for (final o in _items) o.orderId};
        _items.addAll(page.items.where((o) => !known.contains(o.orderId)));
        _total = page.total;
        _page++;
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() => _moreError = loadFailureMessage(e));
    } finally {
      _loadingMore = false;
      if (mounted) setState(() {});
    }
  }

  void _open(OrderSummary order) {
    if (widget.onOpenOrder != null) return widget.onOpenOrder!(order);
    Navigator.of(context)
        .pushNamed(
          BookingRoutes.bookingDetail,
          arguments: OrderDetailArgs(orderId: order.orderId),
        )
        .then((_) {
          if (mounted) _load(); // the order may have been paid or cancelled
        });
  }

  void _book() {
    if (widget.onBook != null) return widget.onBook!();
    Navigator.of(context).pushNamed(BookingRoutes.booking).then((_) {
      if (mounted) _load();
    });
  }

  ShiftOption? _shift(String code) =>
      _options?.shifts.where((s) => s.shiftCode == code).firstOrNull;

  @override
  Widget build(BuildContext context) {
    // A tab of the customer shell (large title) or a pushed screen (MOB-UI-15, DESIGN-SYSTEM §5).
    final pushed = ModalRoute.of(context)?.canPop ?? false;
    final ready = !_loading && _error == null;
    final count = ready
        ? Text(
            _items.length == _total
                ? '$_total đơn'
                : '${_items.length} / $_total đơn',
            key: const Key('history-count'),
            style: NordicTypography.bodyRegular.copyWith(
              color: NordicColors.textSecondary,
            ),
          )
        : null;
    final children = [_filters(), ..._body()];
    if (pushed) {
      return NordicPage.pushed(
        title: 'Đơn đã đặt',
        onRefresh: _load,
        children: [
          if (count != null)
            Padding(
              padding: const EdgeInsets.only(bottom: NordicSpace.xs),
              child: count,
            ),
          ...children,
        ],
      );
    }
    return NordicPage.tab(
      title: 'Đơn',
      headerAction: count,
      onRefresh: _load,
      children: children,
    );
  }

  static const _weekdays = ['T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'CN'];
  static const _weekdayNames = [
    'Thứ Hai',
    'Thứ Ba',
    'Thứ Tư',
    'Thứ Năm',
    'Thứ Sáu',
    'Thứ Bảy',
    'Chủ Nhật',
  ];

  // ---------------------------------------------------------------- filters

  Widget _filters() {
    return Padding(
      padding: const EdgeInsets.only(bottom: NordicSpace.md),
      child: Wrap(
        spacing: NordicSpace.xs,
        runSpacing: NordicSpace.xs,
        children: [
          _chip('history-filter-all', 'Tất cả', null),
          _chip(
            'history-filter-${OrderStatus.completed}',
            'Hoàn thành',
            OrderStatus.completed,
          ),
          _chip(
            'history-filter-${OrderStatus.cancelled}',
            'Đã huỷ',
            OrderStatus.cancelled,
          ),
        ],
      ),
    );
  }

  Widget _chip(String key, String label, String? status) {
    final selected = _filter == status;
    return Pressable(
      child: Semantics(
        button: true,
        selected: selected,
        label: label,
        child: Material(
          color: selected ? NordicColors.primary : NordicColors.surface,
          shape: StadiumBorder(
            side: BorderSide(
              color: selected ? NordicColors.primary : NordicColors.border,
            ),
          ),
          clipBehavior: Clip.antiAlias,
          child: InkWell(
            key: Key(key),
            onTap: () => _setFilter(status),
            child: ConstrainedBox(
              constraints: const BoxConstraints(minHeight: 44, minWidth: 44),
              child: Padding(
                padding: const EdgeInsets.symmetric(horizontal: NordicSpace.md),
                child: Center(
                  widthFactor: 1,
                  child: ExcludeSemantics(
                    child: Text(
                      label,
                      style: NordicTypography.labelMedium.copyWith(
                        fontWeight: selected
                            ? FontWeight.w700
                            : FontWeight.w500,
                        color: selected
                            ? NordicColors.textInverse
                            : NordicColors.textBody,
                      ),
                    ),
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }

  // --------------------------------------------------------------- upcoming

  /// The open orders that have not started, as dark raised cards; only on "Tất cả".
  List<Widget> _upcoming() {
    final options = _options;
    if (_filter != null || options == null) return const [];
    final now = vietnamNow((widget.clock ?? DateTime.now)());
    final upcoming = upcomingOrders(_items, options, now);
    if (upcoming.isEmpty) return const [];
    return [
      Padding(
        padding: const EdgeInsets.only(bottom: NordicSpace.xs),
        child: Text('Sắp tới', style: NordicTypography.titleSmall),
      ),
      for (final order in upcoming) ...[
        _upcomingCard(order),
        const SizedBox(height: NordicSpace.sm),
      ],
      const SizedBox(height: NordicSpace.xs),
    ];
  }

  Widget _upcomingCard(OrderSummary order) {
    final shift = _shift(order.shiftCode);
    final day = DateTime.tryParse(order.scheduledDate);
    final caps = [
      if (day != null)
        '${_weekdayNames[day.weekday - 1]}, ${day.day.toString().padLeft(2, '0')}/${day.month.toString().padLeft(2, '0')}',
      shiftLabel(order.shiftCode),
    ].join(' · ').toUpperCase();
    final muted = NordicTypography.bodySmall.copyWith(
      color: NordicColors.onDarkMuted,
    );
    return Pressable(
      child: DecoratedBox(
        decoration: const BoxDecoration(
          borderRadius: NordicRadius.cardAll,
          boxShadow: NordicElevation.raised,
        ),
        child: Material(
          color: NordicColors.contrastDark,
          shape: const RoundedRectangleBorder(
            borderRadius: NordicRadius.cardAll,
          ),
          clipBehavior: Clip.antiAlias,
          child: InkWell(
            key: Key('history-upcoming-${order.orderId}'),
            onTap: () => _open(order),
            child: Padding(
              padding: const EdgeInsets.all(NordicSpace.md),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: Text(
                          caps,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: NordicTypography.caps.copyWith(
                            color: NordicColors.onDarkMuted,
                          ),
                        ),
                      ),
                      const SizedBox(width: NordicSpace.xs),
                      OrderStatusPill(order.orderStatus),
                    ],
                  ),
                  const SizedBox(height: NordicSpace.sm),
                  Row(
                    crossAxisAlignment: CrossAxisAlignment.end,
                    children: [
                      Flexible(
                        child: Text(
                          shift?.startLocal ?? shiftLabel(order.shiftCode),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: NordicTypography.numberLarge.copyWith(
                            color: NordicColors.textInverse,
                          ),
                        ),
                      ),
                      if (shift != null) ...[
                        const SizedBox(width: NordicSpace.xs),
                        Padding(
                          padding: const EdgeInsets.only(bottom: 4),
                          child: Text('đến ${shift.endLocal}', style: muted),
                        ),
                      ],
                      const Spacer(),
                      Text(
                        formatVnd(order.totalAmount),
                        style: NordicTypography.numberSmall.copyWith(
                          color: NordicColors.textInverse,
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: NordicSpace.xxs),
                  Text(
                    '${tierLabel(order.serviceTier)} · ${order.requiredWorkers} người',
                    style: muted,
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  // ----------------------------------------------------------------- months

  /// Every loaded order under its month; one card per month, one row per order.
  List<Widget> _months() {
    final groups = groupOrdersByMonth(
      _items,
      startOf: (code) => _shift(code)?.startLocal,
    );
    final widgets = <Widget>[];
    for (final group in groups) {
      if (widgets.isNotEmpty)
        widgets.add(const SizedBox(height: NordicSpace.lg));
      if (group.key.isNotEmpty) {
        widgets.add(
          Padding(
            padding: const EdgeInsets.only(bottom: NordicSpace.xs),
            child: Row(
              children: [
                Expanded(
                  child: Text(
                    'Tháng ${group.month}/${group.year}',
                    style: NordicTypography.titleSmall,
                  ),
                ),
                Text(
                  '${group.orders.length} đơn',
                  style: NordicTypography.bodySmall,
                ),
              ],
            ),
          ),
        );
      }
      widgets.add(
        NordicCard(
          padding: EdgeInsets.zero,
          child: Column(
            children: [
              for (var i = 0; i < group.orders.length; i++) ...[
                if (i > 0)
                  const Divider(
                    height: 1,
                    color: NordicColors.surfaceContainer,
                  ),
                _row(group.orders[i]),
              ],
            ],
          ),
        ),
      );
    }
    return widgets;
  }

  Widget _row(OrderSummary order) {
    final day = DateTime.tryParse(order.scheduledDate);
    final cancelled = order.orderStatus == OrderStatus.cancelled;
    final shift = _shift(order.shiftCode);
    final meta = [
      if (shift != null) '${shift.startLocal}–${shift.endLocal}',
      formatVnd(order.totalAmount),
    ].join(' · ');
    return InkWell(
      key: Key('order-${order.orderId}'),
      onTap: () => _open(order),
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 72),
        child: Padding(
          padding: const EdgeInsets.all(NordicSpace.sm),
          child: Row(
            children: [
              Container(
                width: 48,
                padding: const EdgeInsets.symmetric(vertical: NordicSpace.xs),
                decoration: BoxDecoration(
                  color: cancelled
                      ? NordicColors.surfaceLow
                      : NordicColors.primaryFixed,
                  borderRadius: NordicRadius.tileAll,
                ),
                child: Column(
                  children: [
                    Text(
                      day == null ? '–' : day.day.toString().padLeft(2, '0'),
                      style: NordicTypography.numberSmall.copyWith(
                        color: cancelled
                            ? NordicColors.textSecondary
                            : NordicColors.primary,
                      ),
                    ),
                    if (day != null)
                      Text(
                        _weekdays[day.weekday - 1],
                        style: NordicTypography.bodySmall.copyWith(
                          color: cancelled
                              ? NordicColors.textSecondary
                              : NordicColors.primaryContainer,
                        ),
                      ),
                  ],
                ),
              ),
              const SizedBox(width: NordicSpace.sm),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      '${shiftLabel(order.shiftCode)} · ${tierLabel(order.serviceTier)}',
                      style: NordicTypography.titleSmall.copyWith(
                        color: cancelled
                            ? NordicColors.textBody
                            : NordicColors.textTitle,
                      ),
                    ),
                    const SizedBox(height: 2.0),
                    Text(
                      meta,
                      style: NordicTypography.bodyRegular.copyWith(
                        color: NordicColors.textBody,
                      ),
                    ),
                    if (order.orderStatus == OrderStatus.pendingPayment ||
                        order.orderStatus == OrderStatus.awaitingFallback)
                      Text(
                        order.orderStatus == OrderStatus.pendingPayment
                            ? 'Cần thanh toán'
                            : 'Cần bạn chọn lịch',
                        style: NordicTypography.bodySmall.copyWith(
                          color: NordicColors.primary,
                        ),
                      ),
                    Text(
                      'Mã ${order.orderCode}',
                      style: NordicTypography.bodySmall.copyWith(fontSize: 11),
                    ),
                  ],
                ),
              ),
              const SizedBox(width: NordicSpace.xs),
              OrderStatusPill(
                order.orderStatus,
                textKey: Key('order-status-${order.orderId}'),
              ),
            ],
          ),
        ),
      ),
    );
  }

  // ------------------------------------------------------------------- body

  List<Widget> _body() {
    if (_loading) {
      return const [
        SizedBox(
          height: 320,
          child: LoadingStateWidget(message: 'Đang tải đơn…'),
        ),
      ];
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
    if (_items.isEmpty) {
      return [
        switch (_filter) {
          OrderStatus.completed => const EmptyStateWidget(
            title: 'Không có đơn hoàn thành',
            subtitle: 'Đơn xong ca sẽ hiện ở đây.',
          ),
          OrderStatus.cancelled => const EmptyStateWidget(
            title: 'Không có đơn đã huỷ',
            subtitle: 'Đơn bạn huỷ sẽ hiện ở đây.',
          ),
          _ => EmptyStateWidget(
            title: 'Bạn chưa có đơn nào',
            subtitle: 'Đơn bạn đặt sẽ hiện ở đây.',
            action: NordicButton(label: 'Đặt ca', onPressed: _book),
          ),
        },
      ];
    }
    return [
      ..._upcoming(),
      ..._months(),
      const SizedBox(height: NordicSpace.md),
      if (_moreError != null)
        Text(
          _moreError!,
          key: const Key('history-more-error'),
          style: NordicTypography.bodySmall.copyWith(color: NordicColors.error),
        ),
      if (hasMoreOrders(_items.length, _total))
        NordicButton(
          key: const Key('history-more'),
          label: 'Xem thêm',
          width: double.infinity,
          variant: NordicButtonVariant.secondary,
          isLoading: _loadingMore,
          onPressed: _more,
        ),
    ];
  }
}
