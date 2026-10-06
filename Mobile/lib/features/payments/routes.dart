import 'package:flutter/widgets.dart';
import '../../app/feature_module.dart';
import 'screens/payment_screen.dart';

/// Route names owned by the Payments feature module.
class PaymentRoutes {
  PaymentRoutes._();

  static const String payment = '/payments';
  static const String momo = '/payments/momo';
}

/// FeatureModule for Payments (Slot M2).
class PaymentsFeatureModule extends FeatureModule {
  @override
  String get name => 'payments';

  @override
  Map<String, WidgetBuilder> get routes => {
        PaymentRoutes.payment: (context) => const PaymentScreen(),
        PaymentRoutes.momo: (context) => const PaymentScreen(),
      };
}
