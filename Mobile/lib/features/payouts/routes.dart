import 'package:flutter/widgets.dart';
import '../../app/feature_module.dart';
import 'screens/payout_screen.dart';

/// Route names owned by the Payouts feature module.
class PayoutRoutes {
  PayoutRoutes._();

  static const String payouts = '/payouts';
  static const String history = '/payouts/history';
}

/// FeatureModule for Worker Payouts (Slot M6).
class PayoutsFeatureModule extends FeatureModule {
  @override
  String get name => 'payouts';

  @override
  Map<String, WidgetBuilder> get routes => {
        PayoutRoutes.payouts: (context) => const PayoutScreen(),
        PayoutRoutes.history: (context) => const PayoutScreen(),
      };
}
