import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/dispatch/services/dispatch_service.dart';
import '../identity/fake_backend.dart';

void main() {
  group('DispatchService HTTP Tests with FakeBackend', () {
    late FakeBackend backend;
    late ApiClient apiClient;
    late DispatchService service;

    setUp(() async {
      backend = FakeBackend();
      await backend.start();
      apiClient = ApiClient(baseUrl: backend.baseUrl);
      service = DispatchService(apiClient: apiClient, isMock: false);
    });

    tearDown(() async {
      await backend.stop();
    });

    test('getCurrentOffer parses 200 JSON envelope from backend', () async {
      backend.on('GET', '/api/dispatch/offers/current', 200, FakeBackend.ok({
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
        'remainingSeconds': 25,
      }));

      final offer = await service.getCurrentOffer();

      expect(offer, isNotNull);
      expect(offer!.assignmentId, 1001);
      expect(offer.district, 'Quận Cầu Giấy');
      expect(offer.netEarnings, 208000.0);
      expect(offer.remainingSeconds, 25);
    });

    test('getCurrentOffer returns null when 404 or data is null', () async {
      backend.on('GET', '/api/dispatch/offers/current', 200, FakeBackend.ok(null));

      final offer = await service.getCurrentOffer();
      expect(offer, isNull);
    });

    test('acceptOffer sends POST /api/dispatch/offers/1001/accept and parses result', () async {
      backend.on('POST', '/api/dispatch/offers/1001/accept', 200, FakeBackend.ok({
        'assignmentId': 1001,
        'status': 'ASSIGNED',
        'acceptedAt': '2026-10-15T01:00:15Z',
        'bookingSlotLocked': true,
      }));

      final result = await service.acceptOffer(1001);

      expect(result.assignmentId, 1001);
      expect(result.status, 'ASSIGNED');
      expect(result.bookingSlotLocked, isTrue);
    });

    test('declineOffer sends POST /api/dispatch/offers/1001/decline with reason', () async {
      backend.on('POST', '/api/dispatch/offers/1001/decline', 200, FakeBackend.ok({
        'assignmentId': 1001,
        'status': 'DECLINED',
      }));

      await service.declineOffer(1001, reason: 'Không kịp giờ');

      expect(
        backend.requestBodies['POST /api/dispatch/offers/1001/decline'],
        {'reason': 'Không kịp giờ'},
      );
    });
  });
}
