import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/customers/services/favorite_worker_service.dart';

void main() {
  group('FavoriteWorkerService Tests', () {
    late FavoriteWorkerService service;

    setUp(() {
      service = FavoriteWorkerService(isMock: true);
    });

    test('getFavoriteWorkers returns seeded list sorted by addedAt descending', () async {
      final list = await service.getFavoriteWorkers();
      expect(list.length, greaterThanOrEqualTo(3));
      expect(list.first.fullName, 'Lê Thị Cúc'); // most recently added (1 day ago)
    });

    test('addFavoriteWorker adds worker idempotently', () async {
      final added = await service.addFavoriteWorker(999);
      expect(added.workerId, 999);

      final list = await service.getFavoriteWorkers();
      expect(list.any((w) => w.workerId == 999), isTrue);

      // Adding again returns existing without duplicating
      final addedAgain = await service.addFavoriteWorker(999);
      expect(addedAgain.workerId, 999);

      final listAfter = await service.getFavoriteWorkers();
      expect(listAfter.where((w) => w.workerId == 999).length, 1);
    });

    test('removeFavoriteWorker deletes worker idempotently', () async {
      final initial = await service.getFavoriteWorkers();
      final target = initial.first;

      await service.removeFavoriteWorker(target.workerId);

      final after = await service.getFavoriteWorkers();
      expect(after.any((w) => w.workerId == target.workerId), isFalse);

      // Removing again doesn't throw
      await service.removeFavoriteWorker(target.workerId);
    });
  });
}
