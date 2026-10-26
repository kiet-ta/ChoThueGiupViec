import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/dispatch/models/job_offer.dart';
import 'package:mobile/features/dispatch/widgets/job_offer_card.dart';

JobOffer _offer({required bool consecutive, required int remaining}) =>
    JobOffer(
      assignmentId: 1002,
      orderId: 501,
      serviceTier: 'ECONOMY',
      bookingDate: '2026-10-15',
      shiftTime: '13:00-17:00',
      district: 'Quận Cầu Giấy',
      approximateDistanceKm: 3.4,
      estimatedDurationHours: 4,
      grossAmount: 260000,
      commissionRate: 0.2,
      netEarnings: 208000,
      offeredAt: DateTime.utc(2026, 10, 14, 3),
      expiresAt: DateTime.utc(2026, 10, 14, 3, 2),
      remainingSeconds: remaining,
      isConsecutiveShift: consecutive,
    );

Future<void> _pump(WidgetTester tester, JobOffer offer) async {
  tester.view.physicalSize = const Size(390, 1600);
  tester.view.devicePixelRatio = 1.0;
  addTearDown(() {
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
  await tester.pumpWidget(
    MaterialApp(
      theme: NordicTheme.lightTheme,
      home: Scaffold(
        body: SingleChildScrollView(
          child: JobOfferCard(
            offer: offer,
            onAccept: () {},
            onDecline: () {},
            onTimeout: () {},
            isAccepting: false,
            isDeclining: false,
          ),
        ),
      ),
    ),
  );
  await tester.pump();
  addTearDown(() => tester.pumpWidget(const SizedBox.shrink()));
}

void main() {
  group('JobOfferCard second consecutive shift (BR-02 fallback)', () {
    testWidgets('explains the shift and gives the worker 120 seconds', (
      tester,
    ) async {
      await _pump(tester, _offer(consecutive: true, remaining: 118));

      expect(find.byKey(const Key('consecutive_shift_notice')), findsOneWidget);
      expect(find.textContaining('120 giây'), findsOneWidget);
      expect(find.textContaining('Xác nhận ca thứ hai'), findsWidgets);
      expect(find.text('CA THỨ HAI LIÊN TIẾP'), findsOneWidget);
    });

    testWidgets('a normal offer keeps its 30 second card', (tester) async {
      await _pump(tester, _offer(consecutive: false, remaining: 28));

      expect(find.byKey(const Key('consecutive_shift_notice')), findsNothing);
      expect(find.text('Nhận việc'), findsOneWidget);
    });
  });
}
