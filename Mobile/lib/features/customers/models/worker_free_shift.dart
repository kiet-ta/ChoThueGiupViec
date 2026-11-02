/// One shift a favorite worker is free for (`.spec/contracts/customers.md` §2.3, `GET .../availability`).
class WorkerFreeShift {
  /// "yyyy-MM-dd".
  final String date;
  final String shiftCode;

  const WorkerFreeShift({required this.date, required this.shiftCode});

  factory WorkerFreeShift.fromJson(Map<String, dynamic> json) =>
      WorkerFreeShift(
        date: json['date'] as String? ?? '',
        shiftCode: json['shiftCode'] as String? ?? '',
      );
}
