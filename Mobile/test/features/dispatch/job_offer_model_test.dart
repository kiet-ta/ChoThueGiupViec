import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/dispatch/models/job_offer.dart';

void main() {
  group('JobOffer Model Tests', () {
    test('JobOffer fromJson parses fields accurately including 20% commission', () {
      final json = {
        'assignmentId': 1001,
        'orderId': 501,
        'serviceTier': 'ECONOMY',
        'bookingDate': '2026-10-15',
        'shiftTime': '08:00 - 12:00',
        'district': 'Quận Cầu Giấy',
        'approximateDistanceKm': 3.4,
        'estimatedDurationHours': 4.0,
        'grossAmount': 260000.0,
        'commissionRate': 0.20,
        'netEarnings': 208000.0,
        'offeredAt': '2026-10-15T01:00:00Z',
        'expiresAt': '2026-10-15T01:00:30Z',
        'remainingSeconds': 28,
      };

      final offer = JobOffer.fromJson(json);

      expect(offer.assignmentId, 1001);
      expect(offer.orderId, 501);
      expect(offer.serviceTier, 'ECONOMY');
      expect(offer.district, 'Quận Cầu Giấy');
      expect(offer.approximateDistanceKm, 3.4);
      expect(offer.estimatedDurationHours, 4.0);
      expect(offer.grossAmount, 260000.0);
      expect(offer.commissionRate, 0.20);
      expect(offer.netEarnings, 208000.0);
      expect(offer.remainingSeconds, 28);
    });

    test('JobOffer roundtrip toJson maintains data integrity', () {
      final offer = JobOffer(
        assignmentId: 2002,
        orderId: 602,
        serviceTier: 'PREMIUM',
        bookingDate: '2026-10-16',
        shiftTime: '13:00 - 17:00',
        district: 'Quận Ba Đình',
        approximateDistanceKm: 5.2,
        estimatedDurationHours: 4.0,
        grossAmount: 320000.0,
        commissionRate: 0.20,
        netEarnings: 256000.0,
        offeredAt: DateTime.utc(2026, 10, 16, 6, 0),
        expiresAt: DateTime.utc(2026, 10, 16, 6, 0, 30),
        remainingSeconds: 30,
      );

      final json = offer.toJson();
      final roundtrip = JobOffer.fromJson(json);

      expect(roundtrip.assignmentId, offer.assignmentId);
      expect(roundtrip.serviceTier, 'PREMIUM');
      expect(roundtrip.netEarnings, 256000.0);
      expect(roundtrip.district, 'Quận Ba Đình');
    });

    test('AcceptOfferResult fromJson parses valid accept result', () {
      final json = {
        'assignmentId': 1001,
        'status': 'ASSIGNED',
        'acceptedAt': '2026-10-15T01:00:12Z',
        'bookingSlotLocked': true,
      };

      final result = AcceptOfferResult.fromJson(json);

      expect(result.assignmentId, 1001);
      expect(result.status, 'ASSIGNED');
      expect(result.bookingSlotLocked, isTrue);
    });
  });
}
