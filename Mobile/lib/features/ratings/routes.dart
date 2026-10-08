import 'package:flutter/material.dart';
import '../../app/feature_module.dart';
import 'screens/rating_screen.dart';

/// Route names owned by the Ratings feature module.
class RatingRoutes {
  RatingRoutes._();

  /// Opens the rating screen; pass a [RatingScreenArgs] as the route arguments.
  static const String ratings = '/ratings';
  static const String history = '/ratings/history';
}

/// FeatureModule for Two-Way Ratings (Slot M6).
class RatingsFeatureModule extends FeatureModule {
  @override
  String get name => 'ratings';

  @override
  Map<String, WidgetBuilder> get routes => {
        RatingRoutes.ratings: (context) {
          final args = ModalRoute.of(context)?.settings.arguments;
          if (args is RatingScreenArgs) {
            return RatingScreen(role: args.role, assignmentId: args.assignmentId, ratedName: args.ratedName);
          }
          return const _NoJobToRate();
        },
        RatingRoutes.history: (context) => const _NoJobToRate(),
      };
}

/// Shown when the screen is opened without a job: ratings are given from a completed job, never from a menu.
class _NoJobToRate extends StatelessWidget {
  const _NoJobToRate();

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Đánh giá ca làm')),
      body: const Center(
        child: Padding(
          padding: EdgeInsets.all(24.0),
          child: Text(
            'Hãy mở một ca đã hoàn thành để đánh giá. Cửa sổ đánh giá kéo dài 48 giờ sau khi ca kết thúc.',
            textAlign: TextAlign.center,
          ),
        ),
      ),
    );
  }
}
