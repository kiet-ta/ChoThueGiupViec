/// Incident log model matching .spec/contracts/dispatch.md §2.4.
class IncidentLog {
  final int incidentId;
  final int assignmentId;
  final int workerId;
  final String incidentType;
  final String description;
  final String? photoEvidenceUrl;
  final double latitude;
  final double longitude;
  final DateTime reportedAt;
  final bool isPenaltyExempt;
  final String reDispatchStatus;
  final DateTime reDispatchDeadline;
  final int? substituteWorkerId;

  const IncidentLog({
    required this.incidentId,
    required this.assignmentId,
    required this.workerId,
    required this.incidentType,
    required this.description,
    this.photoEvidenceUrl,
    required this.latitude,
    required this.longitude,
    required this.reportedAt,
    required this.isPenaltyExempt,
    required this.reDispatchStatus,
    required this.reDispatchDeadline,
    this.substituteWorkerId,
  });

  factory IncidentLog.fromJson(Map<String, dynamic> json) {
    return IncidentLog(
      incidentId: json['incidentId'] as int? ?? 0,
      assignmentId: json['assignmentId'] as int,
      workerId: json['workerId'] as int? ?? 0,
      incidentType: json['incidentType'] as String? ?? 'ACCIDENT',
      description: json['description'] as String? ?? '',
      photoEvidenceUrl: json['photoEvidenceUrl'] as String?,
      latitude: (json['latitude'] as num?)?.toDouble() ?? 0.0,
      longitude: (json['longitude'] as num?)?.toDouble() ?? 0.0,
      reportedAt: DateTime.tryParse(json['reportedAt'] as String? ?? '') ?? DateTime.now(),
      isPenaltyExempt: json['isPenaltyExempt'] as bool? ?? true,
      reDispatchStatus: json['reDispatchStatus'] as String? ?? 'SEARCHING',
      reDispatchDeadline: DateTime.tryParse(json['reDispatchDeadline'] as String? ?? '') ??
          DateTime.now().add(const Duration(minutes: 5)),
      substituteWorkerId: json['substituteWorkerId'] as int?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'incidentId': incidentId,
      'assignmentId': assignmentId,
      'workerId': workerId,
      'incidentType': incidentType,
      'description': description,
      'photoEvidenceUrl': photoEvidenceUrl,
      'latitude': latitude,
      'longitude': longitude,
      'reportedAt': reportedAt.toIso8601String(),
      'isPenaltyExempt': isPenaltyExempt,
      'reDispatchStatus': reDispatchStatus,
      'reDispatchDeadline': reDispatchDeadline.toIso8601String(),
      'substituteWorkerId': substituteWorkerId,
    };
  }
}
