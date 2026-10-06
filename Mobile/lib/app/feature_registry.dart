import 'package:flutter/widgets.dart';
import 'feature_module.dart';

import '../features/identity/routes.dart';
import '../features/customers/routes.dart';
import '../features/booking/routes.dart';
import '../features/payments/routes.dart';
import '../features/dispatch/routes.dart';
import '../features/workers/routes.dart';
import '../features/agencies/routes.dart';
import '../features/ratings/routes.dart';
import '../features/disputes/routes.dart';
import '../features/payouts/routes.dart';

/// Central registry of all feature modules across the 6 slots.
/// By wiring all 10 domain feature modules here once, `Mobile/lib/app/**`
/// remains frozen while each slot adds screens inside its own feature folder.
class FeatureRegistry {
  FeatureRegistry._();

  /// The list of registered feature modules across all slots.
  static final List<FeatureModule> modules = [
    IdentityFeatureModule(),
    CustomersFeatureModule(),
    BookingFeatureModule(),
    PaymentsFeatureModule(),
    DispatchFeatureModule(),
    WorkersFeatureModule(),
    AgenciesFeatureModule(),
    RatingsFeatureModule(),
    DisputesFeatureModule(),
    PayoutsFeatureModule(),
  ];

  /// Merged map of all routes from all registered modules.
  static Map<String, WidgetBuilder> get routes {
    final Map<String, WidgetBuilder> merged = {};
    for (final module in modules) {
      merged.addAll(module.routes);
    }
    return merged;
  }

  /// Composite [RouteFactory] checking each feature module in order.
  static Route<dynamic>? onGenerateRoute(RouteSettings settings) {
    for (final module in modules) {
      final route = module.onGenerateRoute(settings);
      if (route != null) {
        return route;
      }
    }
    return null;
  }
}
