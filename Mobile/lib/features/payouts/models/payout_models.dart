// Models of the worker income screens (contract .spec/contracts/payouts.md section 2.5; decisions Q04, Q11, G-2).
// Every amount is a whole VND figure computed by the server; the device never recomputes money.

/// One finished job in a month of income (`jobs[]` of the earnings answer).
class EarningsJob {
  final int assignmentId;
  final int orderId;
  final DateTime? completedAt;
  final int grossAmount;
  final int commissionAmount;
  final int netAmount;

  /// True for an approved customer-absence fee (it has no commission).
  final bool absenceFee;

  const EarningsJob({
    required this.assignmentId,
    required this.orderId,
    required this.completedAt,
    required this.grossAmount,
    required this.commissionAmount,
    required this.netAmount,
    required this.absenceFee,
  });

  factory EarningsJob.fromJson(Map<String, dynamic> json) => EarningsJob(
        assignmentId: (json['assignmentId'] as num?)?.toInt() ?? 0,
        orderId: (json['orderId'] as num?)?.toInt() ?? 0,
        completedAt: DateTime.tryParse(json['completedAt'] as String? ?? ''),
        grossAmount: (json['grossAmount'] as num?)?.toInt() ?? 0,
        commissionAmount: (json['commissionAmount'] as num?)?.toInt() ?? 0,
        netAmount: (json['netAmount'] as num?)?.toInt() ?? 0,
        absenceFee: json['absenceFee'] as bool? ?? false,
      );
}

/// Values of `payoutStatus` in the earnings answer: whether the month's batch exists and is paid.
class PayoutStatus {
  static const notBuilt = 'NOT_BUILT';
  static const pending = 'PENDING';
  static const transferred = 'TRANSFERRED';

  PayoutStatus._();
}

/// Income of one month (`GET /api/workers/me/earnings`).
class Earnings {
  /// `YYYY-MM`.
  final String periodMonth;
  final int jobCount;
  final int grossAmount;
  final int commissionAmount;
  final int penaltyAmount;
  final int netAmount;

  /// NOT_BUILT, PENDING or TRANSFERRED.
  final String payoutStatus;
  final List<EarningsJob> jobs;

  const Earnings({
    required this.periodMonth,
    required this.jobCount,
    required this.grossAmount,
    required this.commissionAmount,
    required this.penaltyAmount,
    required this.netAmount,
    required this.payoutStatus,
    required this.jobs,
  });

  factory Earnings.fromJson(Map<String, dynamic> json) => Earnings(
        periodMonth: json['periodMonth'] as String? ?? '',
        jobCount: (json['jobCount'] as num?)?.toInt() ?? 0,
        grossAmount: (json['grossAmount'] as num?)?.toInt() ?? 0,
        commissionAmount: (json['commissionAmount'] as num?)?.toInt() ?? 0,
        penaltyAmount: (json['penaltyAmount'] as num?)?.toInt() ?? 0,
        netAmount: (json['netAmount'] as num?)?.toInt() ?? 0,
        payoutStatus: json['payoutStatus'] as String? ?? PayoutStatus.notBuilt,
        jobs: ((json['jobs'] as List?) ?? const [])
            .whereType<Map<String, dynamic>>()
            .map(EarningsJob.fromJson)
            .toList(growable: false),
      );
}

/// One closed payout item of the worker (`items[]` of `GET /api/workers/me/payouts`).
class PayoutHistoryItem {
  final int batchId;
  final String periodMonth;
  final int netAmount;
  final String itemStatus;
  final DateTime? transferredAt;

  const PayoutHistoryItem({
    required this.batchId,
    required this.periodMonth,
    required this.netAmount,
    required this.itemStatus,
    required this.transferredAt,
  });

  factory PayoutHistoryItem.fromJson(Map<String, dynamic> json) => PayoutHistoryItem(
        batchId: (json['batchId'] as num?)?.toInt() ?? 0,
        periodMonth: json['periodMonth'] as String? ?? '',
        netAmount: (json['netAmount'] as num?)?.toInt() ?? 0,
        itemStatus: json['itemStatus'] as String? ?? '',
        transferredAt: DateTime.tryParse(json['transferredAt'] as String? ?? ''),
      );
}

/// A page of the payout history.
class PayoutHistoryPage {
  final List<PayoutHistoryItem> items;
  final int page;
  final int pageSize;
  final int total;

  const PayoutHistoryPage({required this.items, required this.page, required this.pageSize, required this.total});

  factory PayoutHistoryPage.fromJson(Map<String, dynamic> json) => PayoutHistoryPage(
        items: ((json['items'] as List?) ?? const [])
            .whereType<Map<String, dynamic>>()
            .map(PayoutHistoryItem.fromJson)
            .toList(growable: false),
        page: (json['page'] as num?)?.toInt() ?? 1,
        pageSize: (json['pageSize'] as num?)?.toInt() ?? 0,
        total: (json['total'] as num?)?.toInt() ?? 0,
      );
}
