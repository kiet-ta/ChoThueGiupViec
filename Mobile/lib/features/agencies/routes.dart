import 'package:flutter/widgets.dart';
import '../../app/feature_module.dart';
import 'screens/agency_screen.dart';

/// Route names owned by the Agencies feature module.
class AgencyRoutes {
  AgencyRoutes._();

  static const String roster = '/agencies/roster';
  static const String profile = '/agencies/profile';
}

/// FeatureModule for Agencies & B2B Partners (Slot M5).
class AgenciesFeatureModule extends FeatureModule {
  @override
  String get name => 'agencies';

  @override
  Map<String, WidgetBuilder> get routes => {
        AgencyRoutes.roster: (context) => const AgencyScreen(),
        AgencyRoutes.profile: (context) => const AgencyScreen(),
      };
}
