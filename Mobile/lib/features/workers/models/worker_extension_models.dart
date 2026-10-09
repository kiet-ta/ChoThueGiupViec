/// Models for Worker Extension ("Làm lần 2", BR-08, contract workers.md §2.6).

/// Response payload from worker responding to an extension request (contract workers.md §2.6.1).
class WorkerExtensionResponseDto {
  final int assignmentId;
  final int extensionId;
  final int extraHours;
  final int extraAmount;
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
      extraHours: json['extraHours'] as int? ?? 0,
      extraAmount: json['extraAmount'] as int? ?? 0,
      status: json['status'] as String? ?? 'ACCEPTED',
      newEndTime: json['newEndTime'] as String? ?? '',
    );
  }

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
  final String customerName;
  final String workZone;
  final int extraHours;
  final int extraAmount;
  final String currentEndTime;
  final String newEndTime;

  const WorkerExtensionOffer({
    required this.assignmentId,
    required this.extensionId,
    required this.customerName,
    required this.workZone,
    required this.extraHours,
    required this.extraAmount,
    required this.currentEndTime,
    required this.newEndTime,
  });
}
