import 'package:flutter/material.dart';
import '../../app/feature_module.dart';
import 'screens/dispute_create_screen.dart';
import 'screens/dispute_screen.dart';

/// Route names owned by the Disputes feature module.
class DisputeRoutes {
  DisputeRoutes._();

  static const String disputes = '/disputes';
  static const String create = '/disputes/create';
}

/// FeatureModule for Disputes & Claims (Slot M6).
/// Both routes read [DisputeScreenArgs] from the route arguments: the screen needs to know whether a customer or a worker is signed in.
/// Without them (or without an order for `create`) a short note says disputes are opened from a job.
class DisputesFeatureModule extends FeatureModule {
  @override
  String get name => 'disputes';

  @override
  Map<String, WidgetBuilder> get routes => {
        DisputeRoutes.disputes: (context) {
          final args = ModalRoute.of(context)?.settings.arguments;
          if (args is! DisputeScreenArgs) return const _OpenFromJobNote();
          return DisputeScreen(role: args.role, orderId: args.orderId);
        },
        DisputeRoutes.create: (context) {
          final args = ModalRoute.of(context)?.settings.arguments;
          if (args is! DisputeScreenArgs || args.orderId == null) return const _OpenFromJobNote();
          return DisputeCreateScreen(role: args.role, orderId: args.orderId!);
        },
      };
}

class _OpenFromJobNote extends StatelessWidget {
  const _OpenFromJobNote();

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Khiếu Nại & Tranh Chấp')),
      body: const Center(
        child: Padding(
          padding: EdgeInsets.all(24.0),
          child: Text('Hãy mở khiếu nại từ một ca làm của bạn.', key: Key('dispute-open-from-job'), textAlign: TextAlign.center),
        ),
      ),
    );
  }
}
