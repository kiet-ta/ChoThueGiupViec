import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/app/routes.dart';
import 'package:mobile/features/booking/routes.dart';
import 'package:mobile/features/home/home_actions.dart';
import 'package:mobile/features/workers/routes.dart';

void main() {
  group('homeActionsFor', () {
    test('a customer can book and follow the orders', () {
      final routes = homeActionsFor(SessionRoles.customer).map((a) => a.route);

      expect(routes, contains(BookingRoutes.booking));
      expect(routes, contains(BookingRoutes.orders));
    });

    test('a worker can reach eKYC, the slots, the offers and the earnings', () {
      final routes = homeActionsFor(SessionRoles.worker).map((a) => a.route);

      expect(routes, contains(WorkerRoutes.ekyc));
      expect(routes, contains(WorkerRoutes.slots));
      expect(routes, containsAll(['/dispatch/offers', '/payouts']));
    });

    test('nobody signed in (or an unknown role) gets no entries', () {
      expect(homeActionsFor(null), isEmpty);
      expect(homeActionsFor('Admin'), isEmpty);
    });

    test('every entry opens a route the app really registers', () {
      for (final role in [SessionRoles.customer, SessionRoles.worker]) {
        for (final action in homeActionsFor(role)) {
          expect(
            AppRoutes.routes.containsKey(action.route),
            isTrue,
            reason: '${action.label} -> ${action.route} is not registered',
          );
        }
      }
    });

    test('routeWhenSignedIn opens the screen only for the matching role', () {
      expect(
        routeWhenSignedIn(
          sessionRole: SessionRoles.customer,
          needed: SessionRoles.customer,
          route: '/booking',
        ),
        '/booking',
      );
      expect(
        routeWhenSignedIn(
          sessionRole: null,
          needed: SessionRoles.customer,
          route: '/booking',
        ),
        isNull,
      );
      expect(
        routeWhenSignedIn(
          sessionRole: SessionRoles.worker,
          needed: SessionRoles.customer,
          route: '/booking',
        ),
        isNull,
      );
    });
  });
}
