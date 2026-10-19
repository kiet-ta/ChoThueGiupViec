import 'package:flutter/widgets.dart';

import '../../app/feature_module.dart';
import '../workers/routes.dart';
import 'screens/check_in_screen.dart';
import 'screens/customer_absent_screen.dart';
import 'screens/dispatch_offers_screen.dart';
import 'screens/incident_report_screen.dart';

/// Route names owned by the Dispatch feature module.
class DispatchRoutes {
  DispatchRoutes._();

  static const String offers = '/dispatch/offers';
  static const String checkIn = '/dispatch/check-in';
  static const String customerAbsent = '/dispatch/customer-absent';
  static const String incidentReport = '/dispatch/incident-report';
}

/// FeatureModule for Dispatch & Field Operations (Slot M3).
class DispatchFeatureModule extends FeatureModule {
  @override
  String get name => 'dispatch';

  @override
  Map<String, WidgetBuilder> get routes => {
    DispatchRoutes.offers: (context) => const DispatchOffersScreen(),
    DispatchRoutes.checkIn: (context) => withAssignmentArgument(
      context,
      (id) => CheckInScreen(assignmentId: id),
    ),
    DispatchRoutes.customerAbsent: (context) => CustomerAbsentScreen(
      assignmentId: 1001,
      checkedInAt: DateTime.now().subtract(const Duration(minutes: 16)),
    ),
    DispatchRoutes.incidentReport: (context) =>
        const IncidentReportScreen(assignmentId: 1001),
  };
}
