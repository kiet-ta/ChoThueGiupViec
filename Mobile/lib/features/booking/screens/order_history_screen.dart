import 'package:flutter/material.dart';
import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../logic/booking_logic.dart';
import '../logic/order_logic.dart';
import '../models/booking_models.dart';
import '../routes.dart';
import '../services/order_service.dart';

/// The customer's orders, newest first as the server sends them (MOB-M2-06); contract booking.md 3.4.
class OrderHistoryScreen extends StatefulWidget {
  final IOrderService? service;

  /// What a tap on an order does; by default it opens the order screen and reloads the list on return.
  final void Function(OrderSummary order)? onOpenOrder;

  /// What "Đặt đơn" does when there is no order yet; by default it opens the booking screen.
  final VoidCallback? onBook;

  const OrderHistoryScreen({super.key, this.service, this.onOpenOrder, this.onBook});

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
      final page = await _service.listOrders(page: 1, pageSize: historyPageSize);
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
      final page = await _service.listOrders(page: _page + 1, pageSize: historyPageSize);
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
    Navigator.of(context).pushNamed(BookingRoutes.bookingDetail, arguments: OrderDetailArgs(orderId: order.orderId)).then((_) {
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
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(title: const Text('Lịch sử đơn')),
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
    if (_loading) return const [LoadingStateWidget(message: 'Đang tải đơn…')];
    if (_error != null) return [ErrorStateWidget(title: 'Chưa tải được lịch sử đơn', message: _error!, onRetry: _load)];
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
      Text('${_items.length} / $_total đơn', key: const Key('history-count'), style: NordicTypography.bodySmall),
      const SizedBox(height: 8.0),
      for (final order in _items) ...[
        NordicCard(
          key: Key('order-${order.orderId}'),
          onTap: () => _open(order),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(child: Text(order.orderCode, style: NordicTypography.labelMedium)),
                  Text(orderStatusLabel(order.orderStatus), key: Key('order-status-${order.orderId}'), style: NordicTypography.labelSmall),
                ],
              ),
              const SizedBox(height: 4.0),
              Text(scheduleText(order.scheduledDate, order.shiftCode), style: NordicTypography.bodyRegular),
              const SizedBox(height: 2.0),
              Text(
                '${tierLabel(order.serviceTier)} · ${order.requiredWorkers} thợ · ${formatVnd(order.totalAmount)}',
                style: NordicTypography.bodySmall,
              ),
            ],
          ),
        ),
        const SizedBox(height: 8.0),
      ],
      if (_moreError != null) Text(_moreError!, key: const Key('history-more-error'), style: NordicTypography.bodySmall.copyWith(color: NordicColors.error)),
      if (hasMoreOrders(_items.length, _total))
        NordicButton(
          key: const Key('history-more'),
          label: 'Xem thêm',
          variant: NordicButtonVariant.secondary,
          isLoading: _loadingMore,
          onPressed: _more,
        ),
    ];
  }
}
