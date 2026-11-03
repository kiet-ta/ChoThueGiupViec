/// A job booked for the signed-in worker (contract workers.md §2.3.3). Only the district of the address is known to the app.
class WorkerAssignmentDto {
  final int assignmentId;
  final int orderId;
  final String orderCode;
  final String slotDate; // yyyy-MM-dd
  final String shiftCode; // SHIFT_MORNING, SHIFT_AFTERNOON, SHIFT_EVENING
  final String startTime; // HH:mm
  final String endTime; // HH:mm

  /// ASSIGNED, CHECKED_IN, IN_PROGRESS, AWAITING_ACCEPTANCE or COMPLETED.
  final String status;

  /// ECONOMY or PREMIUM.
  final String serviceTier;
  final String district;

  /// What the worker receives for the job.
  final int payoutAmount;

  const WorkerAssignmentDto({
    required this.assignmentId,
    required this.orderId,
    required this.orderCode,
    required this.slotDate,
    required this.shiftCode,
    required this.startTime,
    required this.endTime,
    required this.status,
    required this.serviceTier,
    required this.district,
    required this.payoutAmount,
  });

  factory WorkerAssignmentDto.fromJson(Map<String, dynamic> json) {
    return WorkerAssignmentDto(
      assignmentId: (json['assignmentId'] as num?)?.toInt() ?? 0,
      orderId: (json['orderId'] as num?)?.toInt() ?? 0,
      orderCode: json['orderCode'] as String? ?? '',
      slotDate: json['slotDate'] as String? ?? '',
      shiftCode: json['shiftCode'] as String? ?? '',
      startTime: json['startTime'] as String? ?? '',
      endTime: json['endTime'] as String? ?? '',
      status: json['status'] as String? ?? '',
      serviceTier: json['serviceTier'] as String? ?? '',
      district: json['district'] as String? ?? '',
      payoutAmount: (json['payoutAmount'] as num?)?.round() ?? 0,
    );
  }

  /// The job is under way: the worker has arrived and is working.
  bool get isUnderWay => status == 'CHECKED_IN' || status == 'IN_PROGRESS';

  /// The worker has not arrived yet.
  bool get isNotStarted => status == 'ASSIGNED';
}

/// A paid extension ("Làm lần 2") that waits for the worker's answer (contract workers.md §2.3.4).
class WorkerPendingExtensionDto {
  final int extensionId;
  final double extraHours; // half hours exist: 1.5
  final int extraAmount; // whole VND

  const WorkerPendingExtensionDto({
    required this.extensionId,
    required this.extraHours,
    required this.extraAmount,
  });

  factory WorkerPendingExtensionDto.fromJson(Map<String, dynamic> json) {
    return WorkerPendingExtensionDto(
      extensionId: (json['extensionId'] as num?)?.toInt() ?? 0,
      extraHours: (json['extraHours'] as num?)?.toDouble() ?? 0.0,
      extraAmount: (json['extraAmount'] as num?)?.round() ?? 0,
    );
  }
}

/// One job of the signed-in worker with the full address of the order, readable from the moment the worker accepted it
/// (contract workers.md §2.3.4, `GET /api/v1/workers/assignments/{id}`).
class WorkerAssignmentDetailDto {
  final int assignmentId;
  final int orderId;
  final String orderCode;
  final String slotDate; // yyyy-MM-dd
  final String shiftCode;
  final String startTime; // HH:mm
  final String endTime; // HH:mm
  final String status;
  final String serviceTier;
  final String addressLine;
  final String district;
  final String city;
  final double latitude;
  final double longitude;
  final int payoutAmount;

  /// Null when no paid extension waits for this worker.
  final WorkerPendingExtensionDto? pendingExtension;

  const WorkerAssignmentDetailDto({
    required this.assignmentId,
    required this.orderId,
    required this.orderCode,
    required this.slotDate,
    required this.shiftCode,
    required this.startTime,
    required this.endTime,
    required this.status,
    required this.serviceTier,
    required this.addressLine,
    required this.district,
    required this.city,
    required this.latitude,
    required this.longitude,
    required this.payoutAmount,
    this.pendingExtension,
  });

  factory WorkerAssignmentDetailDto.fromJson(Map<String, dynamic> json) {
    final pending = json['pendingExtension'];
    return WorkerAssignmentDetailDto(
      assignmentId: (json['assignmentId'] as num?)?.toInt() ?? 0,
      orderId: (json['orderId'] as num?)?.toInt() ?? 0,
      orderCode: json['orderCode'] as String? ?? '',
      slotDate: json['slotDate'] as String? ?? '',
      shiftCode: json['shiftCode'] as String? ?? '',
      startTime: json['startTime'] as String? ?? '',
      endTime: json['endTime'] as String? ?? '',
      status: json['status'] as String? ?? '',
      serviceTier: json['serviceTier'] as String? ?? '',
      addressLine: json['addressLine'] as String? ?? '',
      district: json['district'] as String? ?? '',
      city: json['city'] as String? ?? '',
      latitude: (json['latitude'] as num?)?.toDouble() ?? 0.0,
      longitude: (json['longitude'] as num?)?.toDouble() ?? 0.0,
      payoutAmount: (json['payoutAmount'] as num?)?.round() ?? 0,
      pendingExtension: pending is Map<String, dynamic>
          ? WorkerPendingExtensionDto.fromJson(pending)
          : null,
    );
  }

  /// The address as one line for the screen: street, district, city (empty parts left out).
  String get fullAddress => [
    addressLine,
    district,
    city,
  ].where((part) => part.trim().isNotEmpty).join(', ');
}
