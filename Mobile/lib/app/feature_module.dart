import 'package:flutter/widgets.dart';

/// Contract that every feature module in `lib/features/<name>/` implements.
/// By delegating route registration to each feature module, `Mobile/lib/app/**`
/// remains frozen and isolated from feature-specific route additions.
abstract class FeatureModule {
  /// Unique identifier of the feature module (e.g. 'identity', 'booking', 'dispatch').
  String get name;

  /// Map of route name to [WidgetBuilder] provided by this feature module.
  Map<String, WidgetBuilder> get routes;

  /// Optional dynamic route generator for parameterized or custom routes.
  Route<dynamic>? onGenerateRoute(RouteSettings settings) => null;
}
