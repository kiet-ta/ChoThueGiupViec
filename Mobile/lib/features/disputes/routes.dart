import 'package:flutter/widgets.dart';
import '../../app/feature_module.dart';
import 'screens/dispute_screen.dart';

/// Route names owned by the Disputes feature module.
class DisputeRoutes {
  DisputeRoutes._();

  static const String disputes = '/disputes';
  static const String create = '/disputes/create';
}

/// FeatureModule for Disputes & Claims (Slot M6).
class DisputesFeatureModule extends FeatureModule {
  @override
  String get name => 'disputes';

  @override
  Map<String, WidgetBuilder> get routes => {
        DisputeRoutes.disputes: (context) => const DisputeScreen(),
        DisputeRoutes.create: (context) => const DisputeScreen(),
      };
}
