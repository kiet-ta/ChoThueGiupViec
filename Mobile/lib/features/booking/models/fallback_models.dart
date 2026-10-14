/// `state` values of `GET /api/dispatch/orders/{orderId}/fallback` (dispatch.md 3.2).
class FallbackState {
  static const awaitingCustomer = 'AWAITING_CUSTOMER';
  static const awaitingWorker = 'AWAITING_WORKER';
  static const notWaiting = 'NOT_WAITING';

  FallbackState._();
}

/// `outcome` values of `POST /api/dispatch/orders/{orderId}/fallback/respond`.
class FallbackOutcome {
  static const workerAsked = 'WORKER_ASKED';
  static const cancelled = 'CANCELLED';

  FallbackOutcome._();
}

/// Business codes of the fallback endpoints (dispatch.md 3.2).
class FallbackErrorCode {
  static const notWaiting = 'FALLBACK_NOT_WAITING';
  static const alreadyAnswered = 'FALLBACK_ALREADY_ANSWERED';

  FallbackErrorCode._();
}

/// What the customer is asked while an order has one worker and no second one (BR-02 fallback).
class FallbackStatus {
  final int orderId;
  final String state;
  final int? workerId;
  final String? secondShiftCode;
  final String? secondShiftDate;

  /// AWAITING_CUSTOMER: the shift start (silence until then is a "no"). AWAITING_WORKER: the end of the worker's window.
  final DateTime? deadline;

  const FallbackStatus({
    required this.orderId,
    required this.state,
    this.workerId,
    this.secondShiftCode,
    this.secondShiftDate,
    this.deadline,
  });

  factory FallbackStatus.fromJson(Map<String, dynamic> json) => FallbackStatus(
    orderId: (json['orderId'] as num?)?.toInt() ?? 0,
    state: json['state'] as String? ?? FallbackState.notWaiting,
    workerId: (json['workerId'] as num?)?.toInt(),
    secondShiftCode: json['secondShiftCode'] as String?,
    secondShiftDate: json['secondShiftDate'] as String?,
    deadline: DateTime.tryParse(json['deadlineUtc'] as String? ?? ''),
  );
}

/// The answer to the customer's choice.
class FallbackResponse {
  final int orderId;
  final String outcome;
  final DateTime? workerDeadline;
  final String? message;

  const FallbackResponse({
    required this.orderId,
    required this.outcome,
    this.workerDeadline,
    this.message,
  });

  factory FallbackResponse.fromJson(Map<String, dynamic> json) =>
      FallbackResponse(
        orderId: (json['orderId'] as num?)?.toInt() ?? 0,
        outcome: json['outcome'] as String? ?? '',
        workerDeadline: DateTime.tryParse(
          json['workerDeadlineUtc'] as String? ?? '',
        ),
        message: json['message'] as String?,
      );
}
