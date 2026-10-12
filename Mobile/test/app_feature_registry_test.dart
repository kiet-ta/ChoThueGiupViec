import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/app/app.dart';
import 'package:mobile/app/feature_registry.dart';
import 'package:mobile/app/routes.dart';
import 'package:mobile/features/booking/routes.dart';
import 'package:mobile/features/booking/screens/booking_screen.dart';
import 'package:mobile/features/customers/routes.dart';
import 'package:mobile/features/dispatch/routes.dart';
import 'package:mobile/features/identity/routes.dart';
import 'package:mobile/features/payments/routes.dart';
import 'package:mobile/features/workers/routes.dart';
import 'package:mobile/features/agencies/routes.dart';
import 'package:mobile/features/ratings/routes.dart';
import 'package:mobile/features/disputes/routes.dart';
import 'package:mobile/features/payouts/routes.dart';

void main() {
  group('FeatureRegistry & Route Conventions Tests', () {
    test('contains all 10 required domain modules across 6 slots', () {
      final moduleNames = FeatureRegistry.modules.map((m) => m.name).toList();

      expect(moduleNames, containsAll([
        'identity',
        'customers',
        'booking',
        'payments',
        'dispatch',
        'workers',
        'agencies',
        'ratings',
        'disputes',
        'payouts',
      ]));
      expect(FeatureRegistry.modules.length, 10);
    });

    test('registers all feature route paths in AppRoutes.routes', () {
      final routes = AppRoutes.routes;

      // Identity & Auth
      expect(routes.containsKey(IdentityRoutes.phoneInput), isTrue);
      expect(routes.containsKey(IdentityRoutes.login), isTrue);

      // Customers
      expect(routes.containsKey(CustomerRoutes.addressList), isTrue);
      expect(routes.containsKey(CustomerRoutes.addressForm), isTrue);
      expect(routes.containsKey(CustomerRoutes.customerProfile), isTrue);
      expect(routes.containsKey(CustomerRoutes.favoriteWorkers), isTrue);

      // Booking
      expect(routes.containsKey(BookingRoutes.booking), isTrue);
      expect(routes.containsKey(BookingRoutes.bookingDetail), isTrue);

      // Payments
      expect(routes.containsKey(PaymentRoutes.payment), isTrue);
      expect(routes.containsKey(PaymentRoutes.momo), isTrue);

      // Dispatch
      expect(routes.containsKey(DispatchRoutes.offers), isTrue);
      expect(routes.containsKey(DispatchRoutes.checkIn), isTrue);

      // Workers
      expect(routes.containsKey(WorkerRoutes.profile), isTrue);
      expect(routes.containsKey(WorkerRoutes.ekyc), isTrue);
      expect(routes.containsKey(WorkerRoutes.slots), isTrue);

      // Agencies
      expect(routes.containsKey(AgencyRoutes.roster), isTrue);
      expect(routes.containsKey(AgencyRoutes.profile), isTrue);

      // Ratings
      expect(routes.containsKey(RatingRoutes.ratings), isTrue);
      expect(routes.containsKey(RatingRoutes.history), isTrue);

      // Disputes
      expect(routes.containsKey(DisputeRoutes.disputes), isTrue);
      expect(routes.containsKey(DisputeRoutes.create), isTrue);

      // Payouts
      expect(routes.containsKey(PayoutRoutes.payouts), isTrue);
      expect(routes.containsKey(PayoutRoutes.history), isTrue);
    });

    test('onGenerateRoute handles dynamic parameterized routes', () {
      final otpRoute = AppRoutes.onGenerateRoute(
        const RouteSettings(
          name: IdentityRoutes.otpVerify,
          arguments: {
            'phoneNumber': '0901234567',
          },
        ),
      );
      expect(otpRoute, isNotNull);

      final addressFormRoute = AppRoutes.onGenerateRoute(
        const RouteSettings(
          name: CustomerRoutes.addressForm,
        ),
      );
      expect(addressFormRoute, isNull); // Handled by standard routes map
    });

    testWidgets('ToAmApp mounts and can navigate to booking route', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      await tester.pumpWidget(const ToAmApp());
      await tester.pumpAndSettle();

      // Navigate to Booking route using Navigator
      final navigatorState = tester.state<NavigatorState>(find.byType(Navigator));
      navigatorState.pushNamed(BookingRoutes.booking);
      await tester.pumpAndSettle();

      expect(find.byType(BookingScreen), findsOneWidget);
    });

    testWidgets('ToAmApp can navigate to dispatch route', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      await tester.pumpWidget(const ToAmApp());
      await tester.pumpAndSettle();

      final navigatorState = tester.state<NavigatorState>(find.byType(Navigator));
      navigatorState.pushNamed(DispatchRoutes.offers);
      await tester.pumpAndSettle();

      expect(find.text('Bàn Điều Phối Ca'), findsOneWidget);
    });
  });
}
