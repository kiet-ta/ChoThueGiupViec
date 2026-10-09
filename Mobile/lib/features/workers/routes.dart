import 'package:flutter/widgets.dart';
import '../../app/feature_module.dart';
import 'screens/worker_ekyc_screen.dart';
import 'screens/worker_screen.dart';

/// Route names owned by the Workers feature module.
class WorkerRoutes {
  WorkerRoutes._();

  static const String profile = '/workers/profile';
  static const String ekyc = '/workers/ekyc';
  static const String slots = '/workers/slots';
}

/// FeatureModule for Workers & Quality (Slot M4).
class WorkersFeatureModule extends FeatureModule {
  @override
  String get name => 'workers';

  @override
  Map<String, WidgetBuilder> get routes => {
        WorkerRoutes.profile: (context) => const WorkerScreen(),
        WorkerRoutes.ekyc: (context) => const WorkerEkycScreen(),
        WorkerRoutes.slots: (context) => const WorkerScreen(),
      };
}
