/// Request payload for uploading a job photo (contract workers.md §2.4.1).
class UploadJobPhotoRequest {
  final String photoPhase; // BEFORE | AFTER
  final int angleNo; // 1..5
  final String photoUrl;

  const UploadJobPhotoRequest({
    required this.photoPhase,
    required this.angleNo,
    required this.photoUrl,
  });

  Map<String, dynamic> toJson() => {
        'photoPhase': photoPhase,
        'angleNo': angleNo,
        'photoUrl': photoUrl,
      };
}

/// Response returned from job photo upload/list endpoints (contract workers.md §2.4.1).
class JobPhotoDto {
  final int photoId;
  final int assignmentId;
  final String photoPhase;
  final int angleNo;
  final String photoUrl;
  final double volScore;
  final bool isAccepted;
  final DateTime uploadedAt;

  const JobPhotoDto({
    required this.photoId,
    required this.assignmentId,
    required this.photoPhase,
    required this.angleNo,
    required this.photoUrl,
    required this.volScore,
    required this.isAccepted,
    required this.uploadedAt,
  });

  factory JobPhotoDto.fromJson(Map<String, dynamic> json) {
    return JobPhotoDto(
      photoId: json['photoId'] as int? ?? 0,
      assignmentId: json['assignmentId'] as int? ?? 0,
      photoPhase: json['photoPhase'] as String? ?? 'BEFORE',
      angleNo: json['angleNo'] as int? ?? 1,
      photoUrl: json['photoUrl'] as String? ?? '',
      volScore: (json['volScore'] as num?)?.toDouble() ?? 0.0,
      isAccepted: json['isAccepted'] as bool? ?? false,
      uploadedAt: json['uploadedAt'] != null
          ? DateTime.tryParse(json['uploadedAt'].toString()) ?? DateTime.now()
          : DateTime.now(),
    );
  }
}
