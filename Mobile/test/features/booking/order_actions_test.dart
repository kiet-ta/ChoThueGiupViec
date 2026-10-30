import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/booking/screens/booking_screen.dart';
import 'package:mobile/features/booking/routes.dart';
import 'package:mobile/features/customers/services/favorite_worker_service.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/booking/logic/order_logic.dart';
import 'package:mobile/features/booking/models/booking_models.dart';
import 'package:mobile/features/booking/screens/order_detail_screen.dart';
import 'package:mobile/features/disputes/models/dispute_models.dart';
import 'package:mobile/features/disputes/routes.dart';
import 'package:mobile/features/disputes/screens/dispute_screen.dart';
import 'package:mobile/features/ratings/models/rating_models.dart';
import 'package:mobile/features/ratings/routes.dart';
import 'package:mobile/features/ratings/screens/rating_screen.dart';
import 'package:mobile/features/workers/routes.dart';
import 'package:mobile/features/workers/screens/customer_acceptance_screen.dart';

import 'order_screens_test.dart' show FakeOrderService;

ProgressAssignment _worker(
  int id,
  String status, {
  String? name = 'Nguyễn Thị Lan',
}) => ProgressAssignment(
  assignmentId: id,
  assignmentSeq: 1,
  workerId: id + 50,
  workerName: name,
  workerRatingAvg: 4.8,
  assignmentStatus: status,
  acceptedAt: null,
  completedAt: null,
);

OrderProgress _progress(List<ProgressAssignment> workers) => OrderProgress(
  orderId: 100,
  orderStatus: OrderStatus.assigned,
  requiredWorkers: workers.length,
  assignments: workers,
  extension: null,
);

/// MOB-CUSTOMER-03 (#386): from an order the customer reaches the acceptance, the rating and the dispute with the real ids.
void main() {
  group('what the customer can do about an assignment', () {
    test('only a job waiting for acceptance can be accepted, only a completed one rated', () {
      expect(assignmentActions(AssignmentStatus.awaitingAcceptance), [
        AssignmentAction.reviewAndAccept,
      ]);
      expect(assignmentActions(AssignmentStatus.completed), [
        AssignmentAction.rate,
      ]);
      for (final s in [
        AssignmentStatus.assigned,
        AssignmentStatus.checkedIn,
        AssignmentStatus.inProgress,
        AssignmentStatus.cancelledByWorker,
        AssignmentStatus.absent,
        AssignmentStatus.incident,
        '',
      ]) {
        expect(assignmentActions(s), isEmpty, reason: s);
      }
    });

    test('a dispute needs a job that reached the end or an absence', () {
      expect(
        canDispute(_progress([_worker(1, AssignmentStatus.completed)])),
        isTrue,
      );
      expect(
        canDispute(
          _progress([_worker(1, AssignmentStatus.awaitingAcceptance)]),
        ),
        isTrue,
      );
      expect(
        canDispute(_progress([_worker(1, AssignmentStatus.absent)])),
        isTrue,
      );
      expect(
        canDispute(
          _progress([
            _worker(1, AssignmentStatus.assigned),
            _worker(2, AssignmentStatus.inProgress),
          ]),
        ),
        isFalse,
      );
      expect(canDispute(_progress(const [])), isFalse);
    });
  });

  group('OrderDetailScreen: acceptance, rating and dispute', () {
    Future<List<({String route, Object args})>> pump(
      WidgetTester tester,
      FakeOrderService service, {
      FavoriteWorkerService? favorites,
    }) async {
      tester.view.physicalSize = const Size(390, 3200);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });
      final opened = <({String route, Object args})>[];
      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: OrderDetailScreen(
            args: const OrderDetailArgs(orderId: 100),
            service: service,
            favorites: favorites,
            pollInterval: const Duration(seconds: 5),
            onOpen: (route, args) => opened.add((route: route, args: args)),
          ),
        ),
      );
      await tester.pumpAndSettle();
      addTearDown(() => tester.pumpWidget(const SizedBox.shrink()));
      return opened;
    }

    Future<void> tap(WidgetTester tester, String key) async {
      await tester.ensureVisible(find.byKey(Key(key)));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(Key(key)));
      await tester.pumpAndSettle();
    }

    testWidgets(
      'a job waiting for acceptance opens the acceptance of that assignment',
      (tester) async {
        final service = FakeOrderService()
          ..status = OrderStatus.assigned
          ..workers = [_worker(7, AssignmentStatus.awaitingAcceptance)];
        final opened = await pump(tester, service);

        expect(find.byKey(const Key('accept-7')), findsOneWidget);
        expect(find.byKey(const Key('rate-7')), findsNothing);
        await tap(tester, 'accept-7');

        expect(opened, hasLength(1));
        expect(opened.single.route, WorkerRoutes.customerAcceptance);
        expect((opened.single.args as CustomerAcceptanceArgs).assignmentId, 7);
      },
    );

    testWidgets('a completed job opens a read-only acceptance view', (
      tester,
    ) async {
      final service = FakeOrderService()
        ..status = OrderStatus.completed
        ..workers = [_worker(8, AssignmentStatus.completed)];
      final opened = await pump(tester, service);
      await tap(tester, 'review-acceptance-8');
      expect(opened.single.route, WorkerRoutes.customerAcceptance);
      final args = opened.single.args as CustomerAcceptanceArgs;
      expect(args.assignmentId, 8);
      expect(args.completed, isTrue);
    });

    testWidgets(
      'a completed job opens the rating with the customer role, the assignment and the worker name',
      (tester) async {
        final service = FakeOrderService()
          ..status = OrderStatus.completed
          ..workers = [_worker(8, AssignmentStatus.completed)];
        final opened = await pump(tester, service);

        expect(find.byKey(const Key('accept-8')), findsNothing);
        await tap(tester, 'rate-8');

        expect(opened.single.route, RatingRoutes.ratings);
        final args = opened.single.args as RatingScreenArgs;
        expect(args.role, RatingRole.customer);
        expect(args.assignmentId, 8);
        expect(args.ratedName, 'Nguyễn Thị Lan');
      },
    );

    testWidgets(
      '"Đặt lại với" adds the worker to the favourites first, then opens the form with them picked',
      (tester) async {
        final favorites = FavoriteWorkerService(isMock: true);
        final before = (await favorites.getFavoriteWorkers()).length;
        final service = FakeOrderService()
          ..status = OrderStatus.completed
          ..workers = [_worker(8, AssignmentStatus.completed)];
        final opened = await pump(tester, service, favorites: favorites);

        await tap(tester, 'rebook-8');

        final saved = await favorites.getFavoriteWorkers();
        expect(saved.length, before + 1);
        expect(
          saved.any((w) => w.workerId == 58),
          isTrue,
        ); // workerId = assignment id + 50
        expect(opened.single.route, BookingRoutes.booking);
        final args = opened.single.args as BookingScreenArgs;
        expect(args.preferredWorkerId, 58);
        expect(args.serviceTier, ServiceTier.economy);
      },
    );

    testWidgets('the dispute opens with the customer role and the order id', (
      tester,
    ) async {
      final service = FakeOrderService()
        ..status = OrderStatus.completed
        ..workers = [_worker(8, AssignmentStatus.completed)];
      final opened = await pump(tester, service);

      await tap(tester, 'dispute-order');

      expect(opened.single.route, DisputeRoutes.disputes);
      final args = opened.single.args as DisputeScreenArgs;
      expect(args.role, DisputeRole.customer);
      expect(args.orderId, 100);
    });

    testWidgets('a job that is still going has none of the three buttons', (
      tester,
    ) async {
      final service = FakeOrderService()
        ..status = OrderStatus.assigned
        ..workers = [
          _worker(7, AssignmentStatus.inProgress),
          _worker(9, AssignmentStatus.assigned),
        ];
      final opened = await pump(tester, service);

      expect(find.byKey(const Key('accept-7')), findsNothing);
      expect(find.byKey(const Key('rate-7')), findsNothing);
      expect(find.byKey(const Key('dispute-order')), findsNothing);
      expect(opened, isEmpty);
    });

    testWidgets(
      'with two workers each one has its own buttons and its own id',
      (tester) async {
        final service = FakeOrderService()
          ..status = OrderStatus.assigned
          ..workers = [
            _worker(7, AssignmentStatus.completed),
            _worker(8, AssignmentStatus.awaitingAcceptance, name: null),
          ];
        final opened = await pump(tester, service);

        await tap(tester, 'accept-8');
        await tap(tester, 'rate-7');

        expect((opened[0].args as CustomerAcceptanceArgs).assignmentId, 8);
        expect((opened[1].args as RatingScreenArgs).assignmentId, 7);
      },
    );
  });
}
