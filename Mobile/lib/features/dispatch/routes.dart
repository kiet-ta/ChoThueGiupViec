import 'package:flutter/widgets.dart';
import '../../app/feature_module.dart';
import 'screens/check_in_screen.dart';
import 'screens/dispatch_offers_screen.dart';

/// Route names owned by the Dispatch feature module.
class DispatchRoutes {
  DispatchRoutes._();

  static const String offers = '/dispatch/offers';
  static const String checkIn = '/dispatch/check-in';
}

/// FeatureModule for Dispatch & Field Operations (Slot M3).
class DispatchFeatureModule extends FeatureModule {
  @override
  String get name => 'dispatch';

  @override
  Map<String, WidgetBuilder> get routes => {
        DispatchRoutes.offers: (context) => const DispatchOffersScreen(),
        DispatchRoutes.checkIn: (context) => const CheckInScreen(assignmentId: 1001),
      };
}
