import 'package:flutter/material.dart';

import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/status_pill.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../logic/ledger_logic.dart';
import '../logic/payout_logic.dart';
import '../models/ledger_models.dart';
import '../services/ledger_service.dart';

/// The freelancer's statement of one month (LEDGER-17); contract ledger.md section 9.
/// Opening balance, every entry oldest first, the advance, the penalty, the settlement, the closing balance and the hash
/// of the last row. It is read in the app: no spreadsheet package is added (decision K).
class LedgerStatementScreen extends StatefulWidget {
  /// `YYYY-MM`.
  final String month;
  final ILedgerService? service;

  const LedgerStatementScreen({super.key, required this.month, this.service});

  @override
  State<LedgerStatementScreen> createState() => _LedgerStatementScreenState();
}

class _LedgerStatementScreenState extends State<LedgerStatementScreen> {
  late final ILedgerService _service;

  WorkerStatement? _statement;
  String? _error;
  bool _forbidden = false;
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _service = widget.service ?? LedgerService();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
      _forbidden = false;
    });
    try {
      final statement = await _service.getStatement(widget.month);
      if (!mounted) return;
      setState(() {
        _statement = statement;
        _loading = false;
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _statement = null;
        _error = ledgerFailureMessage(e);
        _forbidden = e.isForbidden;
        _loading = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return NordicPage.pushed(
      title: 'Sao kê ${monthLabel(widget.month).toLowerCase()}',
      onRefresh: _load,
      children: _content(),
    );
  }

  List<Widget> _content() {
    if (_loading) {
      return const [
        SizedBox(
          height: 240.0,
          child: LoadingStateWidget(message: 'Đang tải sao kê…'),
        ),
      ];
    }
    if (_error != null) {
      return [
        ErrorStateWidget(message: _error!, onRetry: _forbidden ? null : _load),
      ];
    }
    final s = _statement!;
    final lines = statementSummary(s);
    return [
      NordicCard(
        child: Column(
          children: [
            for (final line in lines)
              _summaryRow(line, strong: line.key == 'closing'),
          ],
        ),
      ),
      const SizedBox(height: NordicSpace.section),
      const Text(
        'Các giao dịch trong tháng',
        style: NordicTypography.titleSmall,
      ),
      const SizedBox(height: NordicSpace.xs),
      if (s.items.isEmpty)
        const NordicCard(
          child: Text(
            'Tháng này chưa có giao dịch nào.',
            key: Key('statement-empty'),
            style: NordicTypography.bodyRegular,
          ),
        )
      else
        for (final e in s.items) _entryTile(ledgerRow(e)),
      const SizedBox(height: NordicSpace.md),
      NordicCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              'Mã kiểm tra dòng cuối',
              style: NordicTypography.bodySmall,
            ),
            const SizedBox(height: NordicSpace.xxs),
            SelectableText(
              s.lastHash ?? '—',
              key: const Key('statement-hash'),
              style: NordicTypography.bodySmall,
            ),
          ],
        ),
      ),
    ];
  }

  Widget _summaryRow(StatementLine line, {bool strong = false}) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: NordicSpace.xxs),
      child: Row(
        children: [
          Expanded(
            child: Text(
              line.label,
              style: strong
                  ? NordicTypography.titleSmall
                  : NordicTypography.bodyRegular,
            ),
          ),
          Text(
            line.value,
            key: Key('statement-${line.key}'),
            style: NordicTypography.titleSmall,
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
        key: Key('statement-row-${row.entryId}'),
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
                    'Mã ${row.code} · ${row.hash}',
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
