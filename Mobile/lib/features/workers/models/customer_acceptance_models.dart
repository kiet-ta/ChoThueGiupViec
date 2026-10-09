/// Response model returned when customer accepts job completion (contract workers.md §2.5.2).
class AssignmentCompletionDto {
  final int assignmentId;
  final String status; // COMPLETED
  final int grossAmount;
  final double commissionRate;
  final int payoutAmount;
  final DateTime completedAt;

  const AssignmentCompletionDto({
    required this.assignmentId,
    required this.status,
    required this.grossAmount,
    required this.commissionRate,
    required this.payoutAmount,
    required this.completedAt,
  });

  factory AssignmentCompletionDto.fromJson(Map<String, dynamic> json) {
    return AssignmentCompletionDto(
      assignmentId: json['assignmentId'] as int? ?? 0,
      status: json['status'] as String? ?? 'COMPLETED',
      grossAmount: json['grossAmount'] as int? ?? 0,
      commissionRate: (json['commissionRate'] as num?)?.toDouble() ?? 0.20,
      payoutAmount: json['payoutAmount'] as int? ?? 0,
      completedAt: json['completedAt'] != null
          ? DateTime.tryParse(json['completedAt'].toString()) ?? DateTime.now()
          : DateTime.now(),
    );
  }
}

/// Request payload for customer requesting a touch-up/redo (contract workers.md §2.5.3).
class RequestRedoRequest {
  final String reason;
  final String? redoPhotoUrl;

  const RequestRedoRequest({
    required this.reason,
    this.redoPhotoUrl,
  });

  Map<String, dynamic> toJson() => {
        'reason': reason,
        if (redoPhotoUrl != null && redoPhotoUrl!.isNotEmpty) 'redoPhotoUrl': redoPhotoUrl,
      };
}

/// Response returned when requesting a redo or checking assignment status (contract workers.md §2.5.3).
class AssignmentStatusDto {
  final int assignmentId;
  final String status;
  final String? notes;

  const AssignmentStatusDto({
    required this.assignmentId,
    required this.status,
    this.notes,
  });

  factory AssignmentStatusDto.fromJson(Map<String, dynamic> json) {
    return AssignmentStatusDto(
      assignmentId: json['assignmentId'] as int? ?? 0,
      status: json['status'] as String? ?? 'IN_PROGRESS',
      notes: json['notes'] as String?,
    );
  }
}
