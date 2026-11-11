import '../models/wallet_models.dart';
import 'payout_logic.dart';
import 'wallet_logic.dart';

// Pure helpers of the withdrawal history screen: one row per request, ready to show.

/// One request of the history. [tone] is `warn`, `ok`, `bad` or `neutral`; the colour never carries the meaning alone, the label does.
class WithdrawalRow {
  final int id;
  final String amount;
  final String statusLabel;
  final String tone;
  final String sent;

  /// What happened to the request: "Được duyệt lúc …", "Bị từ chối lúc …", "Bạn đã huỷ lúc …" or "Đang chờ Admin duyệt".
  final String decided;

  /// The Admin's reason of a rejection; null for the other statuses or when the Admin gave none.
  final String? reason;
  final bool pending;

  const WithdrawalRow({
    required this.id,
    required this.amount,
    required this.statusLabel,
    required this.tone,
    required this.sent,
    required this.decided,
    required this.reason,
    required this.pending,
  });
}

String _toneOf(String status) {
  switch (status) {
    case WithdrawalStatus.pending:
      return 'warn';
    case WithdrawalStatus.paid:
      return 'ok';
    case WithdrawalStatus.rejected:
      return 'bad';
    default:
      return 'neutral';
  }
}

/// The line that says what became of the request, with the time when the server sent one.
String decidedText(Withdrawal w) {
  final at = w.reviewedAt;
  switch (w.status) {
    case WithdrawalStatus.pending:
      return 'Đang chờ Admin duyệt';
    case WithdrawalStatus.paid:
      return at == null
          ? 'Đã được duyệt'
          : 'Được duyệt lúc ${formatVietnamDateTime(at)}';
    case WithdrawalStatus.rejected:
      return at == null
          ? 'Bị từ chối'
          : 'Bị từ chối lúc ${formatVietnamDateTime(at)}';
    case WithdrawalStatus.cancelled:
      return at == null
          ? 'Bạn đã huỷ yêu cầu'
          : 'Bạn đã huỷ lúc ${formatVietnamDateTime(at)}';
    default:
      return w.status;
  }
}

WithdrawalRow withdrawalRow(Withdrawal w) {
  final reason = w.rejectReason?.trim();
  return WithdrawalRow(
    id: w.withdrawalId,
    amount: formatVnd(w.amount),
    statusLabel: withdrawalStatusLabel(w.status),
    tone: _toneOf(w.status),
    sent: 'Gửi lúc ${formatVietnamDateTime(w.requestedAt)}',
    decided: decidedText(w),
    reason:
        w.status == WithdrawalStatus.rejected &&
            reason != null &&
            reason.isNotEmpty
        ? reason
        : null,
    pending: w.status == WithdrawalStatus.pending,
  );
}

/// "3 yêu cầu" under the title; empty when there is none.
String withdrawalCountText(int total) => total == 0 ? '' : '$total yêu cầu';
