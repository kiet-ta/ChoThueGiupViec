import '../logic/acceptance_logic.dart';

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

  /// ACCEPT-02: the server's acceptance phase and deadlines once the photos are submitted (null before the work).
  final AcceptanceBlock? acceptance;

  /// D35: the 2nd job of a large house (`RESERVED` | `OPEN` | `RELEASED`, the next shift); null for any other job.
  final WorkerContinuationDto? continuation;

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
    this.acceptance,
    this.continuation,
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
      acceptance: AcceptanceBlock.fromJson(json['acceptance']),
      continuation: WorkerContinuationDto.fromJson(json['continuation']),
    );
  }

  /// True once the worker has handed the job over (`submit-completion` is accepted only while `IN_PROGRESS`).
  bool get isSubmittedForAcceptance =>
      status == 'AWAITING_ACCEPTANCE' || status == 'COMPLETED';

  /// The address as one line for the screen: street, district, city (empty parts left out).
  String get fullAddress => [
    addressLine,
    district,
    city,
  ].where((part) => part.trim().isNotEmpty).join(', ');
}

/// D35: the 2nd job of a large house, as the worker sees it. `status`: `RESERVED` (the next-shift slot is held for the worker), `OPEN` (the
/// 2nd job opened) or `RELEASED` (the work ended in the first job).
class WorkerContinuationDto {
  final String status;
  final String? shiftCode;

  const WorkerContinuationDto({required this.status, this.shiftCode});

  static WorkerContinuationDto? fromJson(Object? json) =>
      json is Map<String, dynamic>
      ? WorkerContinuationDto(
          status: json['status'] as String? ?? '',
          shiftCode: json['shiftCode'] as String?,
        )
      : null;

  /// What the worker reads; null once the work ended in the first job (nothing to say).
  String? get note {
    final code = shiftCode;
    if (code == null) return null;
    final shift = switch (code) {
      'SHIFT_MORNING' => 'ca sáng',
      'SHIFT_AFTERNOON' => 'ca chiều',
      'SHIFT_EVENING' => 'ca tối',
      _ => code,
    };
    return switch (status) {
      'RESERVED' =>
        'Nhà lớn: nếu chưa làm xong, bạn làm tiếp ở $shift cùng ngày (lịch đã được giữ cho bạn). Tiền công không đổi.',
      'OPEN' =>
        'Đơn nối tiếp đã mở: bạn làm tiếp ở $shift cùng ngày. Tiền công không đổi.',
      _ => null,
    };
  }
}
