/// A time range of a day ("HH:mm") in which a worker cannot start a job: one of their live jobs, already widened by the travel
/// buffer (BOOK-BUSY-01, decisions D41).
class BusyWindow {
  final String startTime;
  final String endTime;

  const BusyWindow({required this.startTime, required this.endTime});

  factory BusyWindow.fromJson(Map<String, dynamic> json) => BusyWindow(
    startTime: json['startTime'] as String? ?? '',
    endTime: json['endTime'] as String? ?? '',
  );
}

/// One shift a favorite worker is free for (`.spec/contracts/customers.md` §2.3, `GET .../availability`). A timed job keeps the shift
/// open (D34), so [busyWindows] say which times of that day are taken and [remainingHours] how many hours of the day are left.
class WorkerFreeShift {
  /// "yyyy-MM-dd".
  final String date;
  final String shiftCode;
  final List<BusyWindow> busyWindows;

  /// Null = the server did not say.
  final double? remainingHours;

  const WorkerFreeShift({
    required this.date,
    required this.shiftCode,
    this.busyWindows = const [],
    this.remainingHours,
  });

  factory WorkerFreeShift.fromJson(Map<String, dynamic> json) {
    final windows = json['busyWindows'];
    return WorkerFreeShift(
      date: json['date'] as String? ?? '',
      shiftCode: json['shiftCode'] as String? ?? '',
      busyWindows: windows is List
          ? windows
                .whereType<Map<String, dynamic>>()
                .map(BusyWindow.fromJson)
                .toList()
          : const [],
      remainingHours: (json['remainingHours'] as num?)?.toDouble(),
    );
  }
}
