/// Job offer data shape corresponding to .spec/contracts/dispatch.md §2.1.
class JobOffer {
  final int assignmentId;
  final int orderId;
  final String serviceTier;
  final String bookingDate;
  final String shiftTime;
  final String district;
  final double approximateDistanceKm;
  final double estimatedDurationHours;
  final double grossAmount;
  final double commissionRate;
  final double netEarnings;
  final DateTime offeredAt;
  final DateTime expiresAt;
  final int remainingSeconds;

  const JobOffer({
    required this.assignmentId,
    required this.orderId,
    required this.serviceTier,
    required this.bookingDate,
    required this.shiftTime,
    required this.district,
    required this.approximateDistanceKm,
    required this.estimatedDurationHours,
    required this.grossAmount,
    required this.commissionRate,
    required this.netEarnings,
    required this.offeredAt,
    required this.expiresAt,
    required this.remainingSeconds,
  });

  factory JobOffer.fromJson(Map<String, dynamic> json) {
    return JobOffer(
      assignmentId: json['assignmentId'] as int,
      orderId: json['orderId'] as int,
      serviceTier: json['serviceTier'] as String? ?? 'ECONOMY',
      bookingDate: json['bookingDate'] as String? ?? '',
      shiftTime: json['shiftTime'] as String? ?? '',
      district: json['district'] as String? ?? '',
      approximateDistanceKm: (json['approximateDistanceKm'] as num?)?.toDouble() ?? 0.0,
      estimatedDurationHours: (json['estimatedDurationHours'] as num?)?.toDouble() ?? 0.0,
      grossAmount: (json['grossAmount'] as num?)?.toDouble() ?? 0.0,
      commissionRate: (json['commissionRate'] as num?)?.toDouble() ?? 0.20,
      netEarnings: (json['netEarnings'] as num?)?.toDouble() ?? 0.0,
      offeredAt: DateTime.tryParse(json['offeredAt'] as String? ?? '') ?? DateTime.now(),
      expiresAt: DateTime.tryParse(json['expiresAt'] as String? ?? '') ??
          DateTime.now().add(const Duration(seconds: 30)),
      remainingSeconds: json['remainingSeconds'] as int? ?? 30,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'assignmentId': assignmentId,
      'orderId': orderId,
      'serviceTier': serviceTier,
      'bookingDate': bookingDate,
      'shiftTime': shiftTime,
      'district': district,
      'approximateDistanceKm': approximateDistanceKm,
      'estimatedDurationHours': estimatedDurationHours,
      'grossAmount': grossAmount,
      'commissionRate': commissionRate,
      'netEarnings': netEarnings,
      'offeredAt': offeredAt.toIso8601String(),
      'expiresAt': expiresAt.toIso8601String(),
      'remainingSeconds': remainingSeconds,
    };
  }
}

/// Acceptance response shape from .spec/contracts/dispatch.md §3.1.
class AcceptOfferResult {
  final int assignmentId;
  final String status;
  final DateTime acceptedAt;
  final bool bookingSlotLocked;

  const AcceptOfferResult({
    required this.assignmentId,
    required this.status,
    required this.acceptedAt,
    required this.bookingSlotLocked,
  });

  factory AcceptOfferResult.fromJson(Map<String, dynamic> json) {
    return AcceptOfferResult(
      assignmentId: json['assignmentId'] as int,
      status: json['status'] as String? ?? 'ASSIGNED',
      acceptedAt: DateTime.tryParse(json['acceptedAt'] as String? ?? '') ?? DateTime.now(),
      bookingSlotLocked: json['bookingSlotLocked'] as bool? ?? false,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'assignmentId': assignmentId,
      'status': status,
      'acceptedAt': acceptedAt.toIso8601String(),
      'bookingSlotLocked': bookingSlotLocked,
    };
  }
}
