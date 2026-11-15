import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/booking/models/booking_models.dart';
import 'package:mobile/features/booking/screens/order_detail_screen.dart';
import 'package:mobile/features/booking/services/worker_route_service.dart';
import 'package:mobile/features/customers/services/favorite_worker_service.dart';

import 'order_screens_test.dart' show FakeOrderService;

/// MOB-ORDER-03: the "on the way" card and the favourite heart of the order detail.
class _Routes implements IWorkerRouteService {
  final WorkerRouteView? view;
  _Routes(this.view);

  @override
  Future<WorkerRouteView?> getRoute(int assignmentId) async => view;
}

class _FailingFavorites extends FavoriteWorkerService {
  _FailingFavorites() : super(isMock: true);

  @override
  Future<Never> removeFavoriteWorker(int workerId) async =>
      throw Exception('offline');
}

ProgressAssignment _worker(int id, String status) => ProgressAssignment(
  assignmentId: id,
  assignmentSeq: 1,
  workerId: id + 50,
  workerName: 'Võ Thị Hồng',
  workerRatingAvg: 4.9,
  assignmentStatus: status,
  acceptedAt: null,
  completedAt: null,
);

Future<void> _pump(
  WidgetTester tester, {
  required WorkerRouteView? route,
  FavoriteWorkerService? favorites,
}) async {
  tester.view.physicalSize = const Size(390, 1800);
  tester.view.devicePixelRatio = 1.0;
  addTearDown(tester.view.reset);
  final service = FakeOrderService()
    ..status = OrderStatus.assigned
    ..workers = [_worker(7, AssignmentStatus.assigned)];
  await tester.pumpWidget(
    MaterialApp(
      theme: NordicTheme.lightTheme,
      home: OrderDetailScreen(
        args: const OrderDetailArgs(orderId: 100),
        service: service,
        pollInterval: const Duration(hours: 1),
        addressLookup: (_) async => null,
        routeService: _Routes(route),
        favorites: favorites ?? FavoriteWorkerService(isMock: true),
      ),
    ),
  );
  await tester.pumpAndSettle();
  addTearDown(() => tester.pumpWidget(const SizedBox.shrink()));
}

String _text(WidgetTester tester, String key) =>
    tester.widget<Text>(find.byKey(Key(key))).data!;

void main() {
  group('route formats (same as the Web)', () {
    test('distance', () {
      expect(formatRouteDistance(850), '850 m');
      expect(formatRouteDistance(2300), '2,3 km');
    });
    test('duration', () {
      expect(formatRouteDuration(20), '~1 phút');
      expect(formatRouteDuration(720), '~12 phút');
      expect(formatRouteDuration(3600), '~1 giờ');
      expect(formatRouteDuration(3900), '~1 giờ 5 phút');
    });
    test('errors', () {
      expect(
        routeViewForError(
          const ApiException(409, 'x', code: 'WORKER_LOCATION_UNKNOWN'),
        )?.message,
        'Chưa có vị trí của người làm.',
      );
      expect(
        routeViewForError(
          const ApiException(409, 'x', code: 'ROUTE_NOT_AVAILABLE'),
        ),
        isNull,
      );
      expect(routeViewForError(const ApiException(404, 'x')), isNull);
      expect(
        routeViewForError(const ApiException(502, 'x'))?.message,
        routeUnavailableText,
      );
    });
  });

  testWidgets('a worker on the way shows the distance and the time', (
    tester,
  ) async {
    await _pump(
      tester,
      route: const WorkerRouteView.route(
        WorkerRoute(
          assignmentId: 7,
          distanceMeters: 2300,
          durationSeconds: 720,
        ),
      ),
    );

    expect(find.text('Võ Thị Hồng đang trên đường tới'), findsOneWidget);
    expect(_text(tester, 'route-text-7'), 'Cách 2,3 km · ~12 phút');
  });

  testWidgets('no route to show: no card', (tester) async {
    await _pump(tester, route: null);

    expect(find.byKey(const Key('route-7')), findsNothing);
  });

  testWidgets('the heart adds the worker to the favourites', (tester) async {
    final favorites = FavoriteWorkerService(isMock: true);
    await _pump(tester, route: null, favorites: favorites);

    expect(find.byIcon(Icons.favorite_border_rounded), findsOneWidget);
    await tester.tap(find.byKey(const Key('favorite-7')));
    await tester.pumpAndSettle();

    expect(find.byIcon(Icons.favorite_rounded), findsOneWidget);
    expect(
      (await favorites.getFavoriteWorkers()).map((w) => w.workerId),
      contains(57),
    );
  });

  testWidgets('a failed removal puts the heart back and says so', (
    tester,
  ) async {
    final favorites = _FailingFavorites();
    await favorites.addFavoriteWorker(57);
    await _pump(tester, route: null, favorites: favorites);

    expect(find.byIcon(Icons.favorite_rounded), findsOneWidget);
    await tester.tap(find.byKey(const Key('favorite-7')));
    await tester.pumpAndSettle();

    expect(find.byIcon(Icons.favorite_rounded), findsOneWidget);
    expect(_text(tester, 'favorite-error'), contains('Chưa bỏ yêu thích'));
  });
}
