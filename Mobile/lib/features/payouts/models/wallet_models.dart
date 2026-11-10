// Models of the worker wallet (contract .spec/contracts/ledger.md section 6; decisions D28, E).
// Every amount is a whole VND figure computed by the server; the device never recomputes money.

/// Values of `status` of a withdrawal request.
class WithdrawalStatus {
  static const pending = 'PENDING';
  static const paid = 'PAID';
  static const rejected = 'REJECTED';
  static const cancelled = 'CANCELLED';

  WithdrawalStatus._();
}

/// The one request waiting for the Admin (`pendingWithdrawal` of the wallet answer).
class PendingWithdrawal {
  final int withdrawalId;
  final int amount;
  final String status;
  final DateTime? requestedAt;

  const PendingWithdrawal({
    required this.withdrawalId,
    required this.amount,
    required this.status,
    required this.requestedAt,
  });

  factory PendingWithdrawal.fromJson(Map<String, dynamic> json) =>
      PendingWithdrawal(
        withdrawalId: (json['withdrawalId'] as num?)?.toInt() ?? 0,
        amount: (json['amount'] as num?)?.toInt() ?? 0,
        status: json['status'] as String? ?? WithdrawalStatus.pending,
        requestedAt: DateTime.tryParse(json['requestedAt'] as String? ?? ''),
      );
}

/// The worker's wallet (`GET /api/v1/workers/me/wallet`).
class Wallet {
  /// Everything the ledger holds for the worker.
  final int balance;

  /// What can be withdrawn now: earned 24 hours ago or more, no open dispute, minus [held].
  final int available;

  /// The amount of the pending request.
  final int held;
  final PendingWithdrawal? pendingWithdrawal;

  /// When the next part of the balance becomes withdrawable; null when nothing is waiting.
  final DateTime? nextAvailableAt;

  const Wallet({
    required this.balance,
    required this.available,
    required this.held,
    required this.pendingWithdrawal,
    required this.nextAvailableAt,
  });

  factory Wallet.fromJson(Map<String, dynamic> json) {
    final pending = json['pendingWithdrawal'];
    return Wallet(
      balance: (json['balance'] as num?)?.toInt() ?? 0,
      available: (json['available'] as num?)?.toInt() ?? 0,
      held: (json['held'] as num?)?.toInt() ?? 0,
      pendingWithdrawal: pending is Map<String, dynamic>
          ? PendingWithdrawal.fromJson(pending)
          : null,
      nextAvailableAt: DateTime.tryParse(
        json['nextAvailableAt'] as String? ?? '',
      ),
    );
  }
}

/// A request as the server answers a create or a cancel (`WithdrawalDto`).
class Withdrawal {
  final int withdrawalId;
  final int amount;
  final String status;
  final DateTime? requestedAt;
  final DateTime? reviewedAt;
  final String? rejectReason;

  const Withdrawal({
    required this.withdrawalId,
    required this.amount,
    required this.status,
    required this.requestedAt,
    required this.reviewedAt,
    required this.rejectReason,
  });

  factory Withdrawal.fromJson(Map<String, dynamic> json) => Withdrawal(
    withdrawalId: (json['withdrawalId'] as num?)?.toInt() ?? 0,
    amount: (json['amount'] as num?)?.toInt() ?? 0,
    status: json['status'] as String? ?? '',
    requestedAt: DateTime.tryParse(json['requestedAt'] as String? ?? ''),
    reviewedAt: DateTime.tryParse(json['reviewedAt'] as String? ?? ''),
    rejectReason: json['rejectReason'] as String?,
  );
}
