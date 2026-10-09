/// DTO representing an availability booking slot (contract workers.md §2.3.1).
class BookingSlotDto {
  final int slotId;
  final int workerId;
  final String slotDate; // yyyy-MM-dd
  final String shiftCode; // SHIFT_MORNING, SHIFT_AFTERNOON, SHIFT_EVENING
  final String startTime;
  final String endTime;
  final bool isActive;

  const BookingSlotDto({
    required this.slotId,
    required this.workerId,
    required this.slotDate,
    required this.shiftCode,
    required this.startTime,
    required this.endTime,
    required this.isActive,
  });

  factory BookingSlotDto.fromJson(Map<String, dynamic> json) {
    return BookingSlotDto(
      slotId: json['slotId'] as int? ?? 0,
      workerId: json['workerId'] as int? ?? 0,
      slotDate: json['slotDate'] as String? ?? '',
      shiftCode: json['shiftCode'] as String? ?? '',
      startTime: json['startTime'] as String? ?? '',
      endTime: json['endTime'] as String? ?? '',
      isActive: json['isActive'] as bool? ?? true,
    );
  }

  BookingSlotDto copyWith({bool? isActive}) {
    return BookingSlotDto(
      slotId: slotId,
      workerId: workerId,
      slotDate: slotDate,
      shiftCode: shiftCode,
      startTime: startTime,
      endTime: endTime,
      isActive: isActive ?? this.isActive,
    );
  }
}

/// Request payload to toggle availability slot for a given date and shift (contract §2.3.2).
class ToggleBookingSlotRequest {
  final String slotDate; // yyyy-MM-dd
  final String shiftCode; // SHIFT_MORNING, SHIFT_AFTERNOON, SHIFT_EVENING
  final bool isActive;

  const ToggleBookingSlotRequest({
    required this.slotDate,
    required this.shiftCode,
    required this.isActive,
  });

  Map<String, dynamic> toJson() => {
        'slotDate': slotDate,
        'shiftCode': shiftCode,
        'isActive': isActive,
      };
}
