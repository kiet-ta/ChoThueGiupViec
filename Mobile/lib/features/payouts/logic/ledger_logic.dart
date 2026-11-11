import '../../../core/network/api_client.dart';
import '../models/ledger_models.dart';
import 'payout_logic.dart';

// Pure helpers of the worker's transaction history and monthly statement: labels, rows, the month range and the error texts.

const Map<String, String> _entryTypeLabels = {
  'ORDER_PAID': 'Khách thanh toán đơn',
  'EXTENSION_PAID': 'Khách thanh toán làm thêm',
  'JOB_EARNING': 'Tiền công ca',
  'EXTENSION_EARNING': 'Tiền công làm thêm',
  'COMMISSION': 'Phí nền tảng',
  'REFUND': 'Hoàn tiền khách',
  'ABSENCE_FEE': 'Phí khách vắng mặt',
  'PENALTY': 'Khấu trừ khiếu nại',
  'SUBSCRIPTION': 'Mua gói thuê bao',
  'ESCROW_DEPOSIT': 'Nạp ký quỹ',
  'ESCROW_PENALTY': 'Trừ ký quỹ',
  'ESCROW_REVERSAL': 'Hoàn ký quỹ',
  'WITHDRAWAL': 'Rút tiền (ứng lương)',
  'SETTLEMENT': 'Tất toán lương tháng',
  'REVERSAL': 'Bút toán đảo',
};

const Map<String, String> _partyLabels = {
  'EXTERNAL': 'Bên ngoài',
  'PLATFORM_CLEARING': 'Tiền giữ hộ khách',
  'PLATFORM_REVENUE': 'Doanh thu nền tảng',
  'WORKER': 'Thu nhập của bạn',
  'AGENCY': 'Agency',
  'AGENCY_ESCROW': 'Ký quỹ Agency',
};

const Map<String, String> _refLabels = {
  'ORDER': 'Đơn',
  'ASSIGNMENT': 'Ca',
  'DISPUTE': 'Khiếu nại',
  'WITHDRAWAL': 'Yêu cầu rút',
  'PAYOUT_ITEM': 'Kỳ lương',
  'PAYMENT': 'Thanh toán',
  'ESCROW_TXN': 'Ký quỹ',
};

/// The filter chips of the history: all types, then the types a worker meets.
const List<({String? value, String label})> ledgerTypeFilters = [
  (value: null, label: 'Tất cả'),
  (value: 'JOB_EARNING', label: 'Tiền công'),
  (value: 'WITHDRAWAL', label: 'Rút tiền'),
  (value: 'PENALTY', label: 'Khấu trừ'),
  (value: 'SETTLEMENT', label: 'Tất toán'),
];

String entryTypeLabel(String type) => _entryTypeLabels[type] ?? type;

String partyLabel(String type, int? id) {
  final name = _partyLabels[type] ?? type;
  return id == null || type == 'WORKER' ? name : '$name #$id';
}

/// "Ca #10 · đơn #41": what the entry refers to, without internal codes as a title.
String referenceLabel(String refType, int refId, int? orderId) {
  final base = '${_refLabels[refType] ?? refType} #$refId';
  final sameOrder = refType == 'ORDER' && refId == orderId;
  return orderId != null && !sameOrder ? '$base · đơn #$orderId' : base;
}

/// `+221.000 đ` for money coming in, `-300.000 đ` for money going out; a plain amount when the side is unknown.
String signedAmountText(int? signed, int amount) {
  if (signed == null) return formatVnd(amount);
  return '${signed > 0 ? '+' : ''}${formatVnd(signed)}';
}

/// The first 8 characters of a hash and an ellipsis; the whole hash is in the statement.
String shortHash(String? hash) {
  if (hash == null || hash.isEmpty) return '—';
  return hash.length <= 8 ? hash : '${hash.substring(0, 8)}…';
}

/// One line of the history or the statement, ready to show.
class LedgerRow {
  final int entryId;
  final String code;
  final String time;
  final String type;
  final String counterparty;
  final String amount;

  /// `in`, `out` or `plain`: the colour follows the sign, and the sign is in the text too.
  final String tone;
  final String reference;

  /// The monthly settlement closes a month, so the screens highlight it.
  final bool settlement;
  final String hash;

  const LedgerRow({
    required this.entryId,
    required this.code,
    required this.time,
    required this.type,
    required this.counterparty,
    required this.amount,
    required this.tone,
    required this.reference,
    required this.settlement,
    required this.hash,
  });
}

LedgerRow ledgerRow(LedgerEntry e) {
  final signed = e.signedAmount;
  final received = signed != null && signed > 0;
  final other = received
      ? partyLabel(e.fromPartyType, e.fromPartyId)
      : partyLabel(e.toPartyType, e.toPartyId);
  return LedgerRow(
    entryId: e.entryId,
    code: e.entryCode,
    time: formatVietnamDateTime(e.createdAt),
    type: entryTypeLabel(e.entryType),
    counterparty: signed == null
        ? '${partyLabel(e.fromPartyType, e.fromPartyId)} → ${partyLabel(e.toPartyType, e.toPartyId)}'
        : other,
    amount: signedAmountText(signed, e.amount),
    tone: signed == null ? 'plain' : (signed > 0 ? 'in' : 'out'),
    reference: referenceLabel(e.refType, e.refId, e.orderId),
    settlement: e.entryType == 'SETTLEMENT',
    hash: shortHash(e.entryHash),
  );
}

/// The Vietnam days that bound a month: `from` the 1st, `to` the last day (`yyyy-MM-dd`), as the history endpoint expects.
({String from, String to}) monthDayRange(String monthKey) {
  final year = int.parse(monthKey.substring(0, 4));
  final month = int.parse(monthKey.substring(5, 7));
  final lastDay = DateTime.utc(year, month + 1, 0).day;
  return (
    from: '$monthKey-01',
    to: '$monthKey-${lastDay.toString().padLeft(2, '0')}',
  );
}

/// The change of the balance over the period, signed: `+100.000 đ`, `-50.000 đ`, `0 đ`.
String balanceChangeText(int opening, int closing) {
  final change = closing - opening;
  return signedAmountText(change, change);
}

/// Lines of the money summary of a statement, in the order a worker reads them.
class StatementLine {
  final String key;
  final String label;
  final String value;

  const StatementLine(this.key, this.label, this.value);
}

/// Opening balance, advance, penalty, settlement and closing balance of a statement (the lines of entries are listed separately).
List<StatementLine> statementSummary(WorkerStatement s) => [
  StatementLine('opening', 'Số dư đầu tháng', formatVnd(s.openingBalance)),
  StatementLine(
    'advance',
    'Đã ứng trong tháng',
    s.advanceAmount > 0 ? '-${formatVnd(s.advanceAmount)}' : '—',
  ),
  StatementLine(
    'penalty',
    'Khấu trừ khiếu nại',
    s.penaltyAmount > 0 ? '-${formatVnd(s.penaltyAmount)}' : '—',
  ),
  StatementLine(
    'settlement',
    'Tất toán lương tháng',
    s.settlementAmount > 0 ? '-${formatVnd(s.settlementAmount)}' : '—',
  ),
  StatementLine('closing', 'Số dư cuối kỳ', formatVnd(s.closingBalance)),
];

/// What a failed load of the history or the statement is told to the worker.
String ledgerFailureMessage(ApiException e) {
  if (e.isForbidden) {
    return 'Bạn làm việc cho một đại lý nên tiền công được trả cho đại lý, bạn không có số dư riêng.';
  }
  if (e.statusCode == 400) return 'Khoảng thời gian đã chọn không hợp lệ.';
  if (e.isNotFound) return 'Không tìm thấy dữ liệu giao dịch.';
  return e.message;
}
