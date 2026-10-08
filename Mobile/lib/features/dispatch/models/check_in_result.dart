/// Check-in result data shape matching .spec/contracts/dispatch.md §2.2.
class CheckInResult {
  final int checkInId;
  final int assignmentId;
  final DateTime checkedInAt;
  final double distanceMeters;
  final bool isGpsVerified;
  final bool requiresAlternativeVerification;
  final String verificationMethod;
  final String? platePhotoUrl;
  final bool isCustomerConfirmed;

  const CheckInResult({
    required this.checkInId,
    required this.assignmentId,
    required this.checkedInAt,
    required this.distanceMeters,
    required this.isGpsVerified,
    required this.requiresAlternativeVerification,
    required this.verificationMethod,
    this.platePhotoUrl,
    required this.isCustomerConfirmed,
  });

  factory CheckInResult.fromJson(Map<String, dynamic> json) {
    return CheckInResult(
      checkInId: json['checkInId'] as int? ?? 0,
      assignmentId: json['assignmentId'] as int,
      checkedInAt: DateTime.tryParse(json['checkedInAt'] as String? ?? '') ?? DateTime.now(),
      distanceMeters: (json['distanceMeters'] as num?)?.toDouble() ?? 0.0,
      isGpsVerified: json['isGpsVerified'] as bool? ?? false,
      requiresAlternativeVerification:
          json['requiresAlternativeVerification'] as bool? ?? false,
      verificationMethod: json['verificationMethod'] as String? ?? 'GPS',
      platePhotoUrl: json['platePhotoUrl'] as String?,
      isCustomerConfirmed: json['isCustomerConfirmed'] as bool? ?? false,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'checkInId': checkInId,
      'assignmentId': assignmentId,
      'checkedInAt': checkedInAt.toIso8601String(),
      'distanceMeters': distanceMeters,
      'isGpsVerified': isGpsVerified,
      'requiresAlternativeVerification': requiresAlternativeVerification,
      'verificationMethod': verificationMethod,
      'platePhotoUrl': platePhotoUrl,
      'isCustomerConfirmed': isCustomerConfirmed,
    };
  }
}
