import 'package:flutter/material.dart';

import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/status_pill.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../logic/wallet_logic.dart';
import '../logic/withdrawal_history_logic.dart';
import '../models/wallet_models.dart';
import '../services/withdrawal_history_service.dart';

/// The freelancer's own withdrawal requests with the status of each (LEDGER-19); contract ledger.md section 6.
/// Newest first, "Xem thêm" for older ones, pull to refresh. A request that is still waiting is marked so it is easy to follow.
/// Built with the shared Nordic kit only (`Mobile/DESIGN-SYSTEM.md`, `DESIGN-SYSTEM-WORKER.md`).
class WithdrawalHistoryScreen extends StatefulWidget {
  final IWithdrawalHistoryService? service;

  const WithdrawalHistoryScreen({super.key, this.service});

  @override
  State<WithdrawalHistoryScreen> createState() =>
      _WithdrawalHistoryScreenState();
}

class _WithdrawalHistoryScreenState extends State<WithdrawalHistoryScreen> {
  static const int _pageSize = 20;

  late final IWithdrawalHistoryService _service;

  final List<Withdrawal> _items = [];
  int _total = 0;
  int _page = 1;
  String? _error;
  bool _forbidden = false;
  bool _loading = true;
  bool _loadingMore = false;
  String? _moreError;

  /// An answer of an older refresh must not overwrite a newer one.
  int _requestId = 0;

  @override
  void initState() {
    super.initState();
    _service = widget.service ?? WithdrawalHistoryService();
    _load();
  }

  Future<void> _load() async {
    final id = ++_requestId;
    setState(() {
      _loading = true;
      _error = null;
      _forbidden = false;
      _moreError = null;
    });
    try {
      final page = await _service.getWithdrawals(pageSize: _pageSize);
      if (!mounted || id != _requestId) return;
      setState(() {
        _items
          ..clear()
          ..addAll(page.items);
        _total = page.total;
        _page = 1;
        _loading = false;
      });
    } on ApiException catch (e) {
      if (!mounted || id != _requestId) return;
      setState(() {
        _items.clear();
        _error = walletFailureMessage(e);
        _forbidden = e.isForbidden;
        _loading = false;
      });
    }
  }

  Future<void> _loadMore() async {
    if (_loadingMore) return;
    final id = _requestId;
    setState(() {
      _loadingMore = true;
      _moreError = null;
    });
    try {
      final page = await _service.getWithdrawals(
        page: _page + 1,
        pageSize: _pageSize,
      );
      if (!mounted || id != _requestId) return;
      setState(() {
        _items.addAll(page.items);
        _total = page.total;
        _page = page.page;
      });
    } on ApiException catch (e) {
      if (!mounted || id != _requestId) return;
      setState(() => _moreError = walletFailureMessage(e));
    } finally {
      if (mounted) setState(() => _loadingMore = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return NordicPage.pushed(
      title: 'Lịch sử rút tiền',
      onRefresh: _load,
      children: _content(),
    );
  }

  List<Widget> _content() {
    if (_loading) {
      return const [
        SizedBox(
          height: 240.0,
          child: LoadingStateWidget(message: 'Đang tải lịch sử rút tiền…'),
        ),
      ];
    }
    if (_error != null) {
      return [
        ErrorStateWidget(
          message: _error!,
          // Retrying does not help an agency worker; the explanation is the whole answer.
          onRetry: _forbidden ? null : _load,
        ),
      ];
    }
    if (_items.isEmpty) {
      return const [
        NordicCard(
          child: Text(
            'Bạn chưa có yêu cầu rút tiền nào.',
            key: Key('withdrawals-empty'),
            style: NordicTypography.bodyRegular,
          ),
        ),
      ];
    }
    return [
      Text(
        withdrawalCountText(_total),
        key: const Key('withdrawals-count'),
        style: NordicTypography.bodySmall,
      ),
      const SizedBox(height: NordicSpace.xs),
      for (final w in _items) _tile(withdrawalRow(w)),
      if (_moreError != null)
        Padding(
          padding: const EdgeInsets.only(bottom: NordicSpace.xs),
          child: Text(
            _moreError!,
            key: const Key('withdrawals-more-error'),
            style: NordicTypography.bodySmall.copyWith(
              color: NordicColors.error,
            ),
          ),
        ),
      if (_items.length < _total)
        NordicButton(
          key: const Key('withdrawals-more'),
          label: 'Xem thêm',
          variant: NordicButtonVariant.secondary,
          isLoading: _loadingMore,
          onPressed: _loadMore,
        ),
    ];
  }

  PillTone _tone(String tone) {
    switch (tone) {
      case 'ok':
        return PillTone.ok;
      case 'warn':
        return PillTone.warn;
      case 'bad':
        return PillTone.bad;
      default:
        return PillTone.neutral;
    }
  }

  Widget _tile(WithdrawalRow row) {
    return Padding(
      padding: const EdgeInsets.only(bottom: NordicSpace.sm),
      child: NordicCard(
        key: Key('withdrawal-row-${row.id}'),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    row.amount,
                    key: Key('withdrawal-amount-${row.id}'),
                    style: NordicTypography.numberSmall,
                  ),
                ),
                StatusPill(
                  row.statusLabel,
                  dot: true,
                  tone: _tone(row.tone),
                  textKey: Key('withdrawal-status-${row.id}'),
                ),
              ],
            ),
            const SizedBox(height: NordicSpace.xxs),
            Text(row.sent, style: NordicTypography.bodySmall),
            const SizedBox(height: NordicSpace.xxs),
            Text(
              row.decided,
              key: Key('withdrawal-decided-${row.id}'),
              style: row.pending
                  ? NordicTypography.bodySmall.copyWith(
                      color: NordicColors.onAmberSoft,
                    )
                  : NordicTypography.bodySmall,
            ),
            if (row.reason != null) ...[
              const SizedBox(height: NordicSpace.xxs),
              Text(
                'Lý do: ${row.reason}',
                key: Key('withdrawal-reason-${row.id}'),
                style: NordicTypography.bodyRegular,
              ),
            ],
          ],
        ),
      ),
    );
  }
}
