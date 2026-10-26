import 'package:flutter/material.dart';

import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../logic/booking_logic.dart';
import '../logic/order_logic.dart';
import '../models/booking_models.dart';
import '../routes.dart';
import '../services/order_service.dart';
import 'order_status_pill.dart';

/// The customer's orders, newest first as the server sends them (MOB-M2-06); contract booking.md 3.4.
class OrderHistoryScreen extends StatefulWidget {
  final IOrderService? service;

  /// What a tap on an order does; by default it opens the order screen and reloads the list on return.
  final void Function(OrderSummary order)? onOpenOrder;

  /// What "Đặt đơn" does when there is no order yet; by default it opens the booking screen.
  final VoidCallback? onBook;

  const OrderHistoryScreen({
    super.key,
    this.service,
    this.onOpenOrder,
    this.onBook,
  });

  @override
  State<OrderHistoryScreen> createState() => _OrderHistoryScreenState();
}

class _OrderHistoryScreenState extends State<OrderHistoryScreen> {
  late final IOrderService _service;
  final _items = <OrderSummary>[];
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
    setState(() {
      _loading = true;
      _error = null;
      _moreError = null;
    });
    try {
      final page = await _service.listOrders(
        page: 1,
        pageSize: historyPageSize,
      );
      if (!mounted) return;
      setState(() {
        _items
          ..clear()
          ..addAll(page.items);
        _total = page.total;
        _page = 1;
        _loading = false;
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _error = loadFailureMessage(e);
        _loading = false;
      });
    }
  }

  Future<void> _more() async {
    if (_loadingMore) return;
    _loadingMore = true;
    setState(() => _moreError = null);
    try {
      final page = await _service.listOrders(
        page: _page + 1,
        pageSize: historyPageSize,
      );
      if (!mounted) return;
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

  @override
  Widget build(BuildContext context) {
    // ui-templates `customer/orders.html`: a tab of the customer shell (large title) or a pushed screen.
    final pushed = ModalRoute.of(context)?.canPop ?? false;
    return Scaffold(
      appBar: pushed ? AppBar(title: const Text('Lịch sử đơn')) : null,
      body: SafeArea(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 390.0),
            child: ListView(
              padding: const EdgeInsets.fromLTRB(20.0, 8.0, 20.0, 32.0),
              children: [
                if (!pushed)
                  const Padding(
                    padding: EdgeInsets.only(top: 8.0, bottom: 16.0),
                    child: Text('Đơn', style: NordicTypography.screenTitle),
                  ),
                ..._body(),
              ],
            ),
          ),
        ),
      ),
    );
  }

  static const _weekdays = ['T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'CN'];

  /// The orders grouped by the month of their day, in the server's order.
  List<Widget> _rows() {
    final widgets = <Widget>[];
    String? month;
    var group = <Widget>[];
    void flush() {
      if (group.isEmpty) return;
      widgets.add(
        Material(
          color: NordicColors.surface,
          clipBehavior: Clip.antiAlias,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(16.0),
            side: const BorderSide(color: NordicColors.border),
          ),
          child: Column(children: group),
        ),
      );
      group = <Widget>[];
    }

    for (final order in _items) {
      final day = DateTime.tryParse(order.scheduledDate);
      final key = day == null ? '' : 'Tháng ${day.month}/${day.year}';
      if (key != month) {
        flush();
        month = key;
        if (key.isNotEmpty) {
          widgets.add(
            Padding(
              padding: EdgeInsets.only(
                top: widgets.isEmpty ? 16.0 : 24.0,
                bottom: 8.0,
              ),
              child: Text(key.toUpperCase(), style: NordicTypography.caps),
            ),
          );
        }
      }
      if (group.isNotEmpty) group.add(const Divider(height: 1, thickness: 1));
      group.add(_row(order, day));
    }
    flush();
    return widgets;
  }

  Widget _row(OrderSummary order, DateTime? day) {
    return InkWell(
      key: Key('order-${order.orderId}'),
      onTap: () => _open(order),
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16.0, vertical: 12.0),
        child: Row(
          children: [
            SizedBox(
              width: 40.0,
              child: Column(
                children: [
                  Text(
                    day == null ? '–' : day.day.toString().padLeft(2, '0'),
                    style: NordicTypography.numberSmall,
                  ),
                  if (day != null)
                    Text(
                      _weekdays[day.weekday - 1],
                      style: const TextStyle(
                        fontSize: 11.0,
                        color: NordicColors.textSecondary,
                      ),
                    ),
                ],
              ),
            ),
            const SizedBox(width: 12.0),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    '${tierLabel(order.serviceTier)} · ${shiftLabel(order.shiftCode)}',
                    style: NordicTypography.titleSmall,
                  ),
                  const SizedBox(height: 2.0),
                  Text(order.orderCode, style: NordicTypography.bodySmall),
                  Text(
                    '${order.requiredWorkers} thợ · ${formatVnd(order.totalAmount)}',
                    style: NordicTypography.bodySmall,
                  ),
                ],
              ),
            ),
            const SizedBox(width: 8.0),
            OrderStatusPill(
              order.orderStatus,
              textKey: Key('order-status-${order.orderId}'),
            ),
          ],
        ),
      ),
    );
  }

  List<Widget> _body() {
    if (_loading) return const [LoadingStateWidget(message: 'Đang tải đơn…')];
    if (_error != null) {
      return [
        ErrorStateWidget(
          title: 'Chưa tải được lịch sử đơn',
          message: _error!,
          onRetry: _load,
        ),
      ];
    }
    if (_items.isEmpty) {
      return [
        EmptyStateWidget(
          title: 'Bạn chưa có đơn nào',
          subtitle: 'Đơn bạn đặt sẽ hiện ở đây.',
          action: NordicButton(label: 'Đặt đơn', onPressed: _book),
        ),
      ];
    }
    return [
      Text(
        '${_items.length} / $_total đơn',
        key: const Key('history-count'),
        style: NordicTypography.bodySmall,
      ),
      ..._rows(),
      const SizedBox(height: 16.0),
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
