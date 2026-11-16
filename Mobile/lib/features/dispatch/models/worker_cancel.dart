/// What cancelling an accepted job would do (`GET /api/v1/dispatch/assignments/{id}/cancel-preview`, dispatch.md §3.1, decisions D35).
class WorkerCancelPreview {
  final int assignmentId;
  final int cancelCountInWindow;
  final int lockCount;
  final int windowDays;
  final bool willLockAccount;
  final bool autoAcceptOn;
  final DateTime? jobStartAt;

  /// False when the job starts within 30 minutes: the order is cancelled and refunded at once instead of re-dispatched.
  final bool substituteSearchPossible;

  /// D38: the job is a rescue seat of a Premium order (a Super-Freelancer took over). Until its check-in deadline the worker may
  /// cancel it with no penalty; the order is then cancelled and refunded. False when the server does not send the field.
  final bool rescueFreeCancel;

  /// D43: the worker belongs to an agency. Cancelling only tells the agency to replace them (no count, no lock);
  /// [substituteSearchPossible] then means the agency still has time for its swap window. False when the server does not send the field.
  final bool agencyWorker;

  const WorkerCancelPreview({
    required this.assignmentId,
    required this.cancelCountInWindow,
    required this.lockCount,
    required this.windowDays,
    required this.willLockAccount,
    required this.autoAcceptOn,
    required this.jobStartAt,
    required this.substituteSearchPossible,
    this.rescueFreeCancel = false,
    this.agencyWorker = false,
  });

  /// This cancel would be the n-th inside the window.
  int get nextCount => cancelCountInWindow + 1;

  factory WorkerCancelPreview.fromJson(
    Map<String, dynamic> json,
  ) => WorkerCancelPreview(
    assignmentId: (json['assignmentId'] as num?)?.toInt() ?? 0,
    cancelCountInWindow: (json['cancelCountInWindow'] as num?)?.toInt() ?? 0,
    lockCount: (json['lockCount'] as num?)?.toInt() ?? 3,
    windowDays: (json['windowDays'] as num?)?.toInt() ?? 30,
    willLockAccount: json['willLockAccount'] as bool? ?? false,
    autoAcceptOn: json['autoAcceptOn'] as bool? ?? false,
    jobStartAt: DateTime.tryParse(json['jobStartAt'] as String? ?? ''),
    substituteSearchPossible: json['substituteSearchPossible'] as bool? ?? true,
    rescueFreeCancel: json['rescueFreeCancel'] as bool? ?? false,
    agencyWorker: json['agencyWorker'] as bool? ?? false,
  );
}

/// Answer of `POST /api/v1/dispatch/assignments/{id}/cancel` (dispatch.md §3.1, decisions D35).
class WorkerCancelResult {
  static const String redispatching = 'REDISPATCHING';
  static const String cancelledRefunded = 'CANCELLED_REFUNDED';

  /// D43: an agency worker cancelled; the job stays assigned and the agency was told to replace the worker.
  static const String agencyNotified = 'AGENCY_NOTIFIED';

  /// The assignment status of a worker cancel that counts as a violation (Q15).
  static const String cancelledByWorker = 'CANCELLED_BY_WORKER';

  final int assignmentId;
  final String status;
  final int cancelCountInWindow;
  final int lockCount;
  final int windowDays;
  final bool accountLocked;
  final String orderOutcome;

  const WorkerCancelResult({
    required this.assignmentId,
    required this.status,
    required this.cancelCountInWindow,
    required this.lockCount,
    required this.windowDays,
    required this.accountLocked,
    required this.orderOutcome,
  });

  /// D38: the free cancel of a rescue seat ends as `CANCELLED`, not `CANCELLED_BY_WORKER`: no violation was counted.
  bool get isFreeRescueCancel =>
      status.isNotEmpty && status != cancelledByWorker && !isAgencyNotice;

  /// D43: the cancel of an agency worker: nothing was counted, the agency was told.
  bool get isAgencyNotice => orderOutcome == agencyNotified;

  factory WorkerCancelResult.fromJson(Map<String, dynamic> json) =>
      WorkerCancelResult(
        assignmentId: (json['assignmentId'] as num?)?.toInt() ?? 0,
        status: json['status'] as String? ?? '',
        cancelCountInWindow:
            (json['cancelCountInWindow'] as num?)?.toInt() ?? 0,
        lockCount: (json['lockCount'] as num?)?.toInt() ?? 3,
        windowDays: (json['windowDays'] as num?)?.toInt() ?? 30,
        accountLocked: json['accountLocked'] as bool? ?? false,
        orderOutcome: json['orderOutcome'] as String? ?? redispatching,
      );
}

/// Longest reason the server accepts (dispatch.md §3.1).
const int workerCancelReasonMaxLength = 255;

/// The reason problem in plain words, or null when it is fine.
String? validateWorkerCancelReason(String text) {
  final trimmed = text.trim();
  if (trimmed.isEmpty) return 'Bạn cho gia chủ biết lý do huỷ ca nhé.';
  if (trimmed.length > workerCancelReasonMaxLength) {
    return 'Lý do tối đa $workerCancelReasonMaxLength ký tự (đang ${trimmed.length}).';
  }
  return null;
}

/// What the worker reads after cancelling from the schedule (MOB-SLOT-CANCEL-01, decisions D35): what happens to the order, the n-th
/// cancel of the window and, when the account got locked, the way back. The check-in screen shows the same facts on its own screen.
String workerCancelNotice(WorkerCancelResult r) {
  if (r.isAgencyNotice) {
    return 'Đã báo doanh nghiệp. Doanh nghiệp sẽ đổi thợ khác cho ca này.';
  }
  final what = r.orderOutcome == WorkerCancelResult.cancelledRefunded
      ? 'Ca bắt đầu quá gần nên đơn đã được huỷ và gia chủ được hoàn tiền.'
      : 'Hệ thống đã báo gia chủ và đang tìm thợ khác thay bạn.';
  final count =
      'Lần huỷ thứ ${r.cancelCountInWindow}/${r.lockCount} trong ${r.windowDays} ngày.';
  final locked = r.accountLocked
      ? ' Tài khoản đã bị khoá. Liên hệ quản trị viên để được mở lại.'
      : '';
  return 'Đã huỷ ca. $what $count$locked';
}
