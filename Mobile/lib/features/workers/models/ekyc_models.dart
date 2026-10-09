/// Request payload for submitting eKYC photos (contract workers.md §2.2.1).
class SubmitEkycRequest {
  final String frontCccdUrl;
  final String backCccdUrl;
  final String selfieUrl;

  const SubmitEkycRequest({
    required this.frontCccdUrl,
    required this.backCccdUrl,
    required this.selfieUrl,
  });

  Map<String, dynamic> toJson() => {
        'frontCccdUrl': frontCccdUrl,
        'backCccdUrl': backCccdUrl,
        'selfieUrl': selfieUrl,
      };
}

/// Response returned from eKYC verification endpoint (contract workers.md §2.2.1).
class EkycResultResponse {
  final int workerId;
  final double confidenceScore;
  final String kycStatus;
  final bool autoApproved;
  final DateTime? reviewedAt;
  final String? rejectionReason;

  const EkycResultResponse({
    required this.workerId,
    required this.confidenceScore,
    required this.kycStatus,
    required this.autoApproved,
    this.reviewedAt,
    this.rejectionReason,
  });

  factory EkycResultResponse.fromJson(Map<String, dynamic> json) {
    return EkycResultResponse(
      workerId: json['workerId'] as int? ?? 0,
      confidenceScore: (json['confidenceScore'] as num?)?.toDouble() ?? 0.0,
      kycStatus: json['kycStatus'] as String? ?? 'PENDING',
      autoApproved: json['autoApproved'] as bool? ?? false,
      reviewedAt: json['reviewedAt'] != null ? DateTime.tryParse(json['reviewedAt'].toString()) : null,
      rejectionReason: json['rejectionReason'] as String?,
    );
  }
}
