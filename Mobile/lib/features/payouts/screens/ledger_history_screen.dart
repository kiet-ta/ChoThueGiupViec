import 'package:flutter/material.dart';

import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/pressable.dart';
import '../../../core/widgets/status_pill.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../logic/ledger_logic.dart';
import '../logic/payout_logic.dart';
import '../models/ledger_models.dart';
import '../services/ledger_service.dart';
import 'ledger_statement_screen.dart';

/// The freelancer's transaction history (LEDGER-17); contract ledger.md sections 5 and 9, decision D28.
/// One month at a time with an optional type filter; each line shows its code, a signed amount and what it refers to.
/// The month's balances come from the server (they ignore the type filter). Built with the shared Nordic kit only.
class LedgerHistoryScreen extends StatefulWidget {
  final ILedgerService? service;

  /// The clock, so the default month can be tested; the real screen uses the device time.
  final DateTime Function()? now;

  const LedgerHistoryScreen({super.key, this.service, this.now});

  @override
  State<LedgerHistoryScreen> createState() => _LedgerHistoryScreenState();
}

class _LedgerHistoryScreenState extends State<LedgerHistoryScreen> {
  static const int _pageSize = 20;

  late final ILedgerService _service;
  late final String _currentMonth;
  late String _month;
  String? _type;

  LedgerHistoryPage? _first;
  final List<LedgerEntry> _entries = [];
  int _total = 0;
  int _page = 1;
  String? _error;
  bool _forbidden = false;
  bool _loading = true;
  bool _loadingMore = false;
  String? _moreError;

  /// Answers of an older month or filter must not overwrite a newer choice.
  int _requestId = 0;

  @override
  void initState() {
    super.initState();
    _service = widget.service ?? LedgerService();
    _currentMonth = monthKeyOf((widget.now ?? DateTime.now)());
    _month = _currentMonth;
    _load();
  }

  Future<void> _load() async {
    final id = ++_requestId;
    final range = monthDayRange(_month);
    setState(() {
      _loading = true;
      _error = null;
      _forbidden = false;
      _moreError = null;
    });
    try {
      final page = await _service.getLedger(
        from: range.from,
        to: range.to,
        type: _type,
        pageSize: _pageSize,
      );
      if (!mounted || id != _requestId) return;
      setState(() {
        _first = page;
        _entries
          ..clear()
          ..addAll(page.items);
        _total = page.total;
        _page = 1;
        _loading = false;
      });
    } on ApiException catch (e) {
      if (!mounted || id != _requestId) return;
      setState(() {
        _first = null;
        _entries.clear();
        _error = ledgerFailureMessage(e);
        _forbidden = e.isForbidden;
        _loading = false;
      });
    }
  }

  Future<void> _loadMore() async {
    if (_loadingMore) return;
    final id = _requestId;
    final range = monthDayRange(_month);
    setState(() {
      _loadingMore = true;
      _moreError = null;
    });
    try {
      final page = await _service.getLedger(
        from: range.from,
        to: range.to,
        type: _type,
        page: _page + 1,
        pageSize: _pageSize,
      );
      if (!mounted || id != _requestId) return;
      setState(() {
        _entries.addAll(page.items);
        _total = page.total;
        _page = page.page;
      });
    } on ApiException catch (e) {
      if (!mounted || id != _requestId) return;
      setState(() => _moreError = ledgerFailureMessage(e));
    } finally {
      if (mounted) setState(() => _loadingMore = false);
    }
  }

  void _go(int delta) {
    setState(() => _month = shiftMonth(_month, delta));
    _load();
  }

  void _filter(String? type) {
    if (type == _type) return;
    setState(() => _type = type);
    _load();
  }

  @override
  Widget build(BuildContext context) {
    return NordicPage.pushed(
      title: 'Lịch sử giao dịch',
      onRefresh: _load,
      children: [
        _monthSelector(),
        const SizedBox(height: NordicSpace.sm),
        _filters(),
        const SizedBox(height: NordicSpace.md),
        ..._content(),
      ],
    );
  }

  Widget _monthSelector() {
    return Row(
      mainAxisAlignment: MainAxisAlignment.spaceBetween,
      children: [
        IconButton.outlined(
          key: const Key('ledger-prev'),
          tooltip: 'Tháng trước',
          onPressed: canGoPrevious(_month) ? () => _go(-1) : null,
          icon: const Icon(Icons.chevron_left_rounded),
        ),
        Text(
          monthLabel(_month),
          key: const Key('ledger-month'),
          style: NordicTypography.titleSmall,
        ),
        IconButton.outlined(
          key: const Key('ledger-next'),
          tooltip: 'Tháng sau',
          onPressed: canGoNext(_month, _currentMonth) ? () => _go(1) : null,
          icon: const Icon(Icons.chevron_right_rounded),
        ),
      ],
    );
  }

  Widget _filters() {
    return Wrap(
      spacing: NordicSpace.xs,
      runSpacing: NordicSpace.xs,
      children: [
        for (final f in ledgerTypeFilters)
          _chip(f.label, f.value == _type, () => _filter(f.value), f.value),
      ],
    );
  }

  /// A selectable pill; the selected one is also marked with `selected` for screen readers.
  Widget _chip(String label, bool selected, VoidCallback onTap, String? value) {
    return Semantics(
      button: true,
      selected: selected,
      child: Pressable(
        child: GestureDetector(
          key: Key('ledger-filter-${value ?? 'all'}'),
          behavior: HitTestBehavior.opaque,
          onTap: onTap,
          child: Container(
            constraints: const BoxConstraints(minHeight: 44.0),
            padding: const EdgeInsets.symmetric(
              horizontal: NordicSpace.md,
              vertical: NordicSpace.xs,
            ),
            alignment: Alignment.center,
            decoration: BoxDecoration(
              color: selected ? NordicColors.primary : NordicColors.surface,
              borderRadius: NordicRadius.pill,
              border: Border.all(
                color: selected ? NordicColors.primary : NordicColors.border,
              ),
            ),
            child: Text(
              label,
              style: NordicTypography.bodySmall.copyWith(
                color: selected
                    ? NordicColors.textInverse
                    : NordicColors.textTitle,
              ),
            ),
          ),
        ),
      ),
    );
  }

  List<Widget> _content() {
    if (_loading) {
      return const [
        SizedBox(
          height: 240.0,
          child: LoadingStateWidget(message: 'Đang tải giao dịch…'),
        ),
      ];
    }
    if (_error != null) {
      return [
        ErrorStateWidget(message: _error!, onRetry: _forbidden ? null : _load),
      ];
    }
    final first = _first!;
    return [
      _balances(first),
      const SizedBox(height: NordicSpace.md),
      if (_entries.isEmpty)
        const NordicCard(
          child: Text(
            'Tháng này chưa có giao dịch nào.',
            key: Key('ledger-empty'),
            style: NordicTypography.bodyRegular,
          ),
        )
      else
        for (final e in _entries) _entryTile(ledgerRow(e)),
      if (_moreError != null)
        Padding(
          padding: const EdgeInsets.only(bottom: NordicSpace.xs),
          child: Text(
            _moreError!,
            key: const Key('ledger-more-error'),
            style: NordicTypography.bodySmall.copyWith(
              color: NordicColors.error,
            ),
          ),
        ),
      if (_entries.length < _total)
        NordicButton(
          key: const Key('ledger-more'),
          label: 'Xem thêm',
          variant: NordicButtonVariant.secondary,
          isLoading: _loadingMore,
          onPressed: _loadMore,
        ),
      const SizedBox(height: NordicSpace.md),
      NordicButton(
        key: const Key('ledger-statement'),
        label: 'Sao kê ${monthLabel(_month).toLowerCase()}',
        onPressed: () => Navigator.of(context).push(
          MaterialPageRoute<void>(
            builder: (_) =>
                LedgerStatementScreen(month: _month, service: _service),
          ),
        ),
      ),
    ];
  }

  /// Opening and closing balance of the month and the change; they ignore the type filter.
  Widget _balances(LedgerHistoryPage page) {
    Widget cell(String label, String value, Key key) => Expanded(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: NordicTypography.bodySmall),
          const SizedBox(height: NordicSpace.xxs),
          Text(value, key: key, style: NordicTypography.titleSmall),
        ],
      ),
    );
    return NordicCard(
      child: Row(
        children: [
          cell(
            'Đầu tháng',
            formatVnd(page.openingBalance),
            const Key('ledger-opening'),
          ),
          cell(
            'Cuối tháng',
            formatVnd(page.closingBalance),
            const Key('ledger-closing'),
          ),
          cell(
            'Biến động',
            balanceChangeText(page.openingBalance, page.closingBalance),
            const Key('ledger-change'),
          ),
        ],
      ),
    );
  }

  Widget _entryTile(LedgerRow row) {
    final color = row.tone == 'in'
        ? NordicColors.primaryContainer
        : row.tone == 'out'
        ? NordicColors.error
        : NordicColors.textTitle;
    return Padding(
      padding: const EdgeInsets.only(bottom: NordicSpace.sm),
      child: NordicCard(
        key: Key('ledger-row-${row.entryId}'),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(row.type, style: NordicTypography.titleSmall),
                ),
                const SizedBox(width: NordicSpace.xs),
                Text(
                  row.amount,
                  key: Key('ledger-amount-${row.entryId}'),
                  style: NordicTypography.titleSmall.copyWith(color: color),
                ),
              ],
            ),
            const SizedBox(height: NordicSpace.xxs),
            Text(
              '${row.time} · ${row.reference}',
              style: NordicTypography.bodySmall,
            ),
            const SizedBox(height: NordicSpace.xxs),
            Row(
              children: [
                Expanded(
                  child: Text(
                    'Mã ${row.code} · ${row.counterparty}',
                    style: NordicTypography.bodySmall,
                  ),
                ),
                if (row.settlement)
                  const StatusPill('Tất toán', tone: PillTone.ok),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
