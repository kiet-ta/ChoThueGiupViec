import 'package:flutter/material.dart';

import '../../app/feature_module.dart';
import 'screens/customer_acceptance_screen.dart';
import 'screens/worker_ekyc_screen.dart';
import 'screens/worker_execution_screen.dart';
import 'screens/worker_extension_screen.dart';
import 'screens/worker_photos_screen.dart';
import 'screens/worker_screen.dart';
import 'screens/worker_slots_screen.dart';

/// Route names owned by the Workers feature module.
class WorkerRoutes {
  WorkerRoutes._();

  static const String profile = '/workers/profile';
  static const String ekyc = '/workers/ekyc';
  static const String slots = '/workers/slots';
  static const String execution = '/workers/execution';
  static const String photos = '/workers/photos';
  static const String extension = '/workers/extension';
  static const String customerAcceptance = '/customers/assignments/acceptance';
}

/// FeatureModule for Workers & Quality (Slot M4).
class WorkersFeatureModule extends FeatureModule {
  @override
  String get name => 'workers';

  @override
  Map<String, WidgetBuilder> get routes => {
    WorkerRoutes.profile: (context) => const WorkerScreen(),
    WorkerRoutes.ekyc: (context) => const WorkerEkycScreen(),
    WorkerRoutes.slots: (context) => const WorkerSlotsScreen(),
    WorkerRoutes.execution: (context) => withAssignmentArgument(
      context,
      (id) => WorkerExecutionScreen(assignmentId: id),
    ),
    WorkerRoutes.photos: (context) => withAssignmentArgument(
      context,
      (id) => WorkerPhotosScreen(assignmentId: id),
    ),
    WorkerRoutes.extension: (context) => const WorkerExtensionScreen(),
    WorkerRoutes.customerAcceptance: (context) =>
        const CustomerAcceptanceScreen(),
  };
}

/// Field-work screens take the accepted assignment id (an `int`) as route argument; the offer and check-in screens push it.
Widget withAssignmentArgument(
  BuildContext context,
  Widget Function(int) build,
) {
  final id = ModalRoute.of(context)?.settings.arguments;
  if (id is int) return build(id);
  return const Scaffold(
    body: Center(child: Text('Hãy mở màn này từ một ca đã nhận.')),
  );
}
