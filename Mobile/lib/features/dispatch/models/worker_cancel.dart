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

  const WorkerCancelPreview({
    required this.assignmentId,
    required this.cancelCountInWindow,
    required this.lockCount,
    required this.windowDays,
    required this.willLockAccount,
    required this.autoAcceptOn,
    required this.jobStartAt,
    required this.substituteSearchPossible,
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
  );
}

/// Answer of `POST /api/v1/dispatch/assignments/{id}/cancel` (dispatch.md §3.1, decisions D35).
class WorkerCancelResult {
  static const String redispatching = 'REDISPATCHING';
  static const String cancelledRefunded = 'CANCELLED_REFUNDED';

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
