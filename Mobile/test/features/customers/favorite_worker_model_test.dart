import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/theme/nordic_colors.dart';
import 'package:mobile/features/customers/models/favorite_worker.dart';

void main() {
  group('FavoriteWorker Model Tests', () {
    test('deserializes from JSON and serializes to JSON correctly', () {
      final json = {
        'workerId': 101,
        'fullName': 'Nguyễn Thị Mai',
        'ratingAvg': 4.95,
        'completedJobs': 142,
        'workStatus': 'ACTIVE',
        'addedAt': '2026-10-06T12:00:00Z',
      };

      final worker = FavoriteWorker.fromJson(json);

      expect(worker.workerId, 101);
      expect(worker.fullName, 'Nguyễn Thị Mai');
      expect(worker.ratingAvg, 4.95);
      expect(worker.completedJobs, 142);
      expect(worker.workStatus, 'ACTIVE');
      expect(worker.addedAt, DateTime.parse('2026-10-06T12:00:00Z'));
      expect(worker.statusDisplayName, 'Sẵn sàng nhận ca');
      expect(worker.statusColor, NordicColors.success);

      final map = worker.toJson();
      expect(map['workerId'], 101);
      expect(map['fullName'], 'Nguyễn Thị Mai');
      expect(map['workStatus'], 'ACTIVE');
    });

    test('maps workStatus values to user-friendly display labels and colors', () {
      const busy = FavoriteWorker(
        workerId: 1,
        fullName: 'A',
        ratingAvg: 4.8,
        completedJobs: 10,
        workStatus: 'BUSY',
      );
      expect(busy.statusDisplayName, 'Đang trong ca làm');
      expect(busy.statusColor, NordicColors.accentAmber);

      const offline = FavoriteWorker(
        workerId: 2,
        fullName: 'B',
        ratingAvg: 4.7,
        completedJobs: 5,
        workStatus: 'OFFLINE',
      );
      expect(offline.statusDisplayName, 'Nghỉ ca');
      expect(offline.statusColor, NordicColors.textSecondary);
    });

    test('copyWith updates specified fields cleanly', () {
      const worker = FavoriteWorker(
        workerId: 1,
        fullName: 'Cũ',
        ratingAvg: 4.5,
        completedJobs: 10,
        workStatus: 'OFFLINE',
      );

      final updated = worker.copyWith(
        fullName: 'Mới',
        workStatus: 'ACTIVE',
      );

      expect(updated.fullName, 'Mới');
      expect(updated.workStatus, 'ACTIVE');
      expect(updated.workerId, 1);
      expect(worker.fullName, 'Cũ');
    });
  });
}
