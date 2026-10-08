/// Customer absent report model matching .spec/contracts/dispatch.md §2.3.
class CustomerAbsentReport {
  final int assignmentId;
  final DateTime reportedAt;
  final DateTime checkedInAt;
  final int elapsedMinutes;
  final int callAttempts;
  final String reviewStatus;
  final double workerFeeRate;
  final double absenceFeeAmount;
  final double customerRefundRate;
  final double customerRefundAmount;

  const CustomerAbsentReport({
    required this.assignmentId,
    required this.reportedAt,
    required this.checkedInAt,
    required this.elapsedMinutes,
    required this.callAttempts,
    required this.reviewStatus,
    required this.workerFeeRate,
    required this.absenceFeeAmount,
    required this.customerRefundRate,
    required this.customerRefundAmount,
  });

  factory CustomerAbsentReport.fromJson(Map<String, dynamic> json) {
    return CustomerAbsentReport(
      assignmentId: json['assignmentId'] as int,
      reportedAt: DateTime.tryParse(json['reportedAt'] as String? ?? '') ?? DateTime.now(),
      checkedInAt: DateTime.tryParse(json['checkedInAt'] as String? ?? '') ?? DateTime.now(),
      elapsedMinutes: json['elapsedMinutes'] as int? ?? 0,
      callAttempts: json['callAttempts'] as int? ?? 0,
      reviewStatus: json['reviewStatus'] as String? ?? 'PENDING_APPROVAL',
      workerFeeRate: (json['workerFeeRate'] as num?)?.toDouble() ?? 0.40,
      absenceFeeAmount: (json['absenceFeeAmount'] as num?)?.toDouble() ?? 0.0,
      customerRefundRate: (json['customerRefundRate'] as num?)?.toDouble() ?? 0.60,
      customerRefundAmount: (json['customerRefundAmount'] as num?)?.toDouble() ?? 0.0,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'assignmentId': assignmentId,
      'reportedAt': reportedAt.toIso8601String(),
      'checkedInAt': checkedInAt.toIso8601String(),
      'elapsedMinutes': elapsedMinutes,
      'callAttempts': callAttempts,
      'reviewStatus': reviewStatus,
      'workerFeeRate': workerFeeRate,
      'absenceFeeAmount': absenceFeeAmount,
      'customerRefundRate': customerRefundRate,
      'customerRefundAmount': customerRefundAmount,
    };
  }
}

/// Call log response shape matching .spec/contracts/dispatch.md §3.1.
class CallLogResult {
  final int assignmentId;
  final int callAttempts;
  final DateTime loggedAt;

  const CallLogResult({
    required this.assignmentId,
    required this.callAttempts,
    required this.loggedAt,
  });

  factory CallLogResult.fromJson(Map<String, dynamic> json) {
    return CallLogResult(
      assignmentId: json['assignmentId'] as int,
      callAttempts: json['callAttempts'] as int? ?? 0,
      loggedAt: DateTime.tryParse(json['loggedAt'] as String? ?? '') ?? DateTime.now(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'assignmentId': assignmentId,
      'callAttempts': callAttempts,
      'loggedAt': loggedAt.toIso8601String(),
    };
  }
}
