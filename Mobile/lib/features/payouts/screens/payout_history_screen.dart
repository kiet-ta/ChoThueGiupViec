import 'package:flutter/material.dart';
import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../logic/payout_logic.dart';
import '../models/payout_models.dart';
import '../services/payouts_service.dart';

/// "Lịch sử giải ngân": the worker's own closed payout items, newest first, loaded page by page (payouts.md 2.5).
class PayoutHistoryScreen extends StatefulWidget {
  final IPayoutsService? service;
  final int pageSize;

  const PayoutHistoryScreen({super.key, this.service, this.pageSize = 20});

  @override
  State<PayoutHistoryScreen> createState() => _PayoutHistoryScreenState();
}

class _PayoutHistoryScreenState extends State<PayoutHistoryScreen> {
  late final IPayoutsService _service;
  final List<PayoutHistoryItem> _items = [];

  int _total = 0;
  int _page = 0;
  bool _loading = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _service = widget.service ?? PayoutsService();
    _loadNext();
  }

  bool get _hasMore => _page == 0 || _items.length < _total;

  Future<void> _loadNext() async {
    if (_loading) return; // one request at a time
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final page = await _service.getPayouts(page: _page + 1, pageSize: widget.pageSize);
      if (!mounted) return;
      setState(() {
        _page = page.page;
        _total = page.total;
        _items.addAll(page.items);
        _loading = false;
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _error = earningsFailureMessage(e);
        _loading = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(title: const Text('Lịch sử giải ngân')),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: _body(),
        ),
      ),
    );
  }

  Widget _body() {
    if (_items.isEmpty && _loading) return const LoadingStateWidget(message: 'Đang tải lịch sử…');
    if (_items.isEmpty && _error != null) return ErrorStateWidget(message: _error!, onRetry: _loadNext);
    if (_items.isEmpty) {
      return ListView(
        padding: const EdgeInsets.all(16.0),
        children: const [
          NordicCard(
            child: Text(
              'Chưa có đợt giải ngân nào. Thu nhập được chuyển theo tháng, sau khi tháng kết thúc.',
              key: Key('history-empty'),
              style: NordicTypography.bodyRegular,
            ),
          ),
        ],
      );
    }
    return ListView(
      padding: const EdgeInsets.all(16.0),
      children: [
        for (final item in _items) _tile(item),
        if (_error != null)
          Padding(
            padding: const EdgeInsets.only(bottom: 8.0),
            child: Text(_error!, key: const Key('history-error'), style: NordicTypography.bodySmall.copyWith(color: NordicColors.error)),
          ),
        if (_hasMore)
          NordicButton(
            key: const Key('history-more'),
            label: 'Xem thêm',
            variant: NordicButtonVariant.secondary,
            isLoading: _loading,
            onPressed: _loading ? null : _loadNext,
          ),
      ],
    );
  }

  Widget _tile(PayoutHistoryItem item) {
    final transferred = item.itemStatus == 'TRANSFERRED';
    final key = item.periodMonth.length == 7 ? monthLabel(item.periodMonth) : item.periodMonth;
    return Padding(
      padding: const EdgeInsets.only(bottom: 8.0),
      child: NordicCard(
        child: Row(
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(key, style: NordicTypography.bodyLarge),
                  const SizedBox(height: 2.0),
                  Text(
                    transferred ? 'Đã chuyển khoản ${formatVietnamDate(item.transferredAt)}' : 'Chờ chuyển khoản',
                    style: NordicTypography.bodySmall,
                  ),
                ],
              ),
            ),
            Text(formatVnd(item.netAmount), key: Key('history-net-${item.batchId}'), style: NordicTypography.bodyLarge),
          ],
        ),
      ),
    );
  }
}
