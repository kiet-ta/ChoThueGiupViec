import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/dispatch/models/job_offer.dart';
import 'package:mobile/features/dispatch/services/dispatch_service.dart';

void main() {
  group('DispatchService Mock Mode Tests', () {
    late DispatchService service;

    setUp(() {
      service = DispatchService(isMock: true);
    });

    test('getCurrentOffer returns default seeded mock offer', () async {
      final offer = await service.getCurrentOffer();

      expect(offer, isNotNull);
      expect(offer!.assignmentId, 1001);
      expect(offer.district, 'Quận Cầu Giấy');
      expect(offer.netEarnings, 208000.0);
      expect(offer.commissionRate, 0.20);
      expect(offer.estimatedDurationHours, 4.0);
    });

    test('acceptOffer successfully accepts active offer and locks slot', () async {
      final result = await service.acceptOffer(1001);

      expect(result.assignmentId, 1001);
      expect(result.status, 'ASSIGNED');
      expect(result.bookingSlotLocked, isTrue);

      // Offer should now be consumed
      final remainingOffer = await service.getCurrentOffer();
      expect(remainingOffer, isNull);
    });

    test('declineOffer clears current offer', () async {
      await service.declineOffer(1001, reason: 'Quá xa');

      final remainingOffer = await service.getCurrentOffer();
      expect(remainingOffer, isNull);
    });

    test('setMockOffer updates pending offer', () async {
      final customOffer = JobOffer(
        assignmentId: 9999,
        orderId: 888,
        serviceTier: 'PREMIUM',
        bookingDate: '2026-10-20',
        shiftTime: '14:00 - 18:00',
        district: 'Quận Đống Đa',
        approximateDistanceKm: 1.5,
        estimatedDurationHours: 4.0,
        grossAmount: 300000.0,
        commissionRate: 0.20,
        netEarnings: 240000.0,
        offeredAt: DateTime.now(),
        expiresAt: DateTime.now().add(const Duration(seconds: 30)),
        remainingSeconds: 30,
      );

      service.setMockOffer(customOffer);
      final offer = await service.getCurrentOffer();

      expect(offer?.assignmentId, 9999);
      expect(offer?.district, 'Quận Đống Đa');
      expect(offer?.netEarnings, 240000.0);
    });
  });
}
