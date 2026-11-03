// Models for Worker Extension ("Làm lần 2", BR-08, contract workers.md §2.6).

import 'worker_assignment_models.dart';

/// Response payload from worker responding to an extension request (contract workers.md §2.6.1).
class WorkerExtensionResponseDto {
  final int assignmentId;
  final int extensionId;
  final double extraHours; // half hours exist: 1.5
  final int extraAmount; // whole VND
  final String status; // ACCEPTED | DECLINED
  final String newEndTime;

  const WorkerExtensionResponseDto({
    required this.assignmentId,
    required this.extensionId,
    required this.extraHours,
    required this.extraAmount,
    required this.status,
    required this.newEndTime,
  });

  factory WorkerExtensionResponseDto.fromJson(Map<String, dynamic> json) {
    return WorkerExtensionResponseDto(
      assignmentId: json['assignmentId'] as int? ?? 0,
      extensionId: json['extensionId'] as int? ?? 0,
      extraHours: _number(json['extraHours']),
      extraAmount: _number(json['extraAmount']).round(),
      status: json['status'] as String? ?? 'ACCEPTED',
      newEndTime: json['newEndTime'] as String? ?? '',
    );
  }

  /// The server's hours and amount are decimals: `2` and `2.0` and `97500.0` all arrive.
  static double _number(Object? value) => value is num ? value.toDouble() : 0.0;

  Map<String, dynamic> toJson() => {
    'assignmentId': assignmentId,
    'extensionId': extensionId,
    'extraHours': extraHours,
    'extraAmount': extraAmount,
    'status': status,
    'newEndTime': newEndTime,
  };
}

/// Information model representing an extension offer to the worker.
class WorkerExtensionOffer {
  final int assignmentId;
  final int extensionId;

  /// The card title: the order code. The customer's name is not sent to the worker (contract workers.md §2.3.4).
  final String title;
  final String workZone;
  final double extraHours;
  final int extraAmount;
  final String currentEndTime;
  final String newEndTime;

  const WorkerExtensionOffer({
    required this.assignmentId,
    required this.extensionId,
    required this.title,
    required this.workZone,
    required this.extraHours,
    required this.extraAmount,
    required this.currentEndTime,
    required this.newEndTime,
  });

  /// The offer the server holds for this job: its paid, unanswered extension (contract workers.md §2.3.4). Null when there is none.
  /// The new end time is the end of the shift plus the extra hours.
  static WorkerExtensionOffer? fromDetail(WorkerAssignmentDetailDto detail) {
    final pending = detail.pendingExtension;
    if (pending == null) return null;
    return WorkerExtensionOffer(
      assignmentId: detail.assignmentId,
      extensionId: pending.extensionId,
      title: 'Đơn ${detail.orderCode}',
      workZone: detail.fullAddress,
      extraHours: pending.extraHours,
      extraAmount: pending.extraAmount,
      currentEndTime: detail.endTime,
      newEndTime: _addHours(detail.endTime, pending.extraHours),
    );
  }

  /// `17:00` plus `2` hours is `19:00`; `1,5` hours is 90 minutes; past midnight wraps to the next day's clock.
  static String _addHours(String hhmm, double hours) {
    final parts = hhmm.split(':');
    if (parts.length != 2) return hhmm;
    final h = int.tryParse(parts[0]);
    final m = int.tryParse(parts[1]);
    if (h == null || m == null) return hhmm;
    final total = (h * 60 + m + (hours * 60).round()) % (24 * 60);
    final hh = (total ~/ 60).toString().padLeft(2, '0');
    final mm = (total % 60).toString().padLeft(2, '0');
    return '$hh:$mm';
  }
}
