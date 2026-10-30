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
