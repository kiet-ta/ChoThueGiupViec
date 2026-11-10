// Models of the worker's transaction history and monthly statement (contract .spec/contracts/ledger.md sections 5 and 9; decision D28).
// Every amount is a whole VND figure computed by the server; the device never recomputes money.

/// One ledger entry as the worker sees it (`items[]` of the history and of the statement).
class LedgerEntry {
  final int entryId;
  final String entryCode;
  final DateTime? createdAt;
  final String entryType;
  final String fromPartyType;
  final int? fromPartyId;
  final String toPartyType;
  final int? toPartyId;
  final int amount;
  final String refType;
  final int refId;
  final int? orderId;
  final String entryHash;

  /// Positive when the money came in to the worker's wallet, negative when it went out; null when the server sent none.
  final int? signedAmount;

  const LedgerEntry({
    required this.entryId,
    required this.entryCode,
    required this.createdAt,
    required this.entryType,
    required this.fromPartyType,
    required this.fromPartyId,
    required this.toPartyType,
    required this.toPartyId,
    required this.amount,
    required this.refType,
    required this.refId,
    required this.orderId,
    required this.entryHash,
    required this.signedAmount,
  });

  factory LedgerEntry.fromJson(Map<String, dynamic> json) => LedgerEntry(
    entryId: (json['entryId'] as num?)?.toInt() ?? 0,
    entryCode: json['entryCode'] as String? ?? '',
    createdAt: DateTime.tryParse(json['createdAt'] as String? ?? ''),
    entryType: json['entryType'] as String? ?? '',
    fromPartyType: json['fromPartyType'] as String? ?? '',
    fromPartyId: (json['fromPartyId'] as num?)?.toInt(),
    toPartyType: json['toPartyType'] as String? ?? '',
    toPartyId: (json['toPartyId'] as num?)?.toInt(),
    amount: (json['amount'] as num?)?.toInt() ?? 0,
    refType: json['refType'] as String? ?? '',
    refId: (json['refId'] as num?)?.toInt() ?? 0,
    orderId: (json['orderId'] as num?)?.toInt(),
    entryHash: json['entryHash'] as String? ?? '',
    signedAmount: (json['signedAmount'] as num?)?.toInt(),
  );

  static List<LedgerEntry> listFrom(Object? raw) => ((raw as List?) ?? const [])
      .whereType<Map<String, dynamic>>()
      .map(LedgerEntry.fromJson)
      .toList(growable: false);
}

/// A page of the history with the balances around the period (`GET /api/v1/workers/me/ledger`).
class LedgerHistoryPage {
  final List<LedgerEntry> items;
  final int openingBalance;
  final int closingBalance;
  final int page;
  final int pageSize;
  final int total;

  const LedgerHistoryPage({
    required this.items,
    required this.openingBalance,
    required this.closingBalance,
    required this.page,
    required this.pageSize,
    required this.total,
  });

  factory LedgerHistoryPage.fromJson(Map<String, dynamic> json) =>
      LedgerHistoryPage(
        items: LedgerEntry.listFrom(json['items']),
        openingBalance: (json['openingBalance'] as num?)?.toInt() ?? 0,
        closingBalance: (json['closingBalance'] as num?)?.toInt() ?? 0,
        page: (json['page'] as num?)?.toInt() ?? 1,
        pageSize: (json['pageSize'] as num?)?.toInt() ?? 0,
        total: (json['total'] as num?)?.toInt() ?? 0,
      );
}

/// A month as it is settled (`GET /api/v1/workers/me/statements/{yyyy-MM}`).
class WorkerStatement {
  /// `YYYY-MM`.
  final String periodMonth;
  final int openingBalance;

  /// The entries of the month oldest first, then the settlement entry written at the start of the next month.
  final List<LedgerEntry> items;
  final int advanceAmount;
  final int penaltyAmount;
  final int settlementAmount;

  /// The balance after the settlement.
  final int closingBalance;

  /// The hash of the last row; a way to check the page against the ledger.
  final String? lastHash;

  const WorkerStatement({
    required this.periodMonth,
    required this.openingBalance,
    required this.items,
    required this.advanceAmount,
    required this.penaltyAmount,
    required this.settlementAmount,
    required this.closingBalance,
    required this.lastHash,
  });

  factory WorkerStatement.fromJson(Map<String, dynamic> json) =>
      WorkerStatement(
        periodMonth: json['periodMonth'] as String? ?? '',
        openingBalance: (json['openingBalance'] as num?)?.toInt() ?? 0,
        items: LedgerEntry.listFrom(json['items']),
        advanceAmount: (json['advanceAmount'] as num?)?.toInt() ?? 0,
        penaltyAmount: (json['penaltyAmount'] as num?)?.toInt() ?? 0,
        settlementAmount: (json['settlementAmount'] as num?)?.toInt() ?? 0,
        closingBalance: (json['closingBalance'] as num?)?.toInt() ?? 0,
        lastHash: json['lastHash'] as String?,
      );
}
