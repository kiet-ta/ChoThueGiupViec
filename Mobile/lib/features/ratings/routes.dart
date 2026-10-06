import 'package:flutter/widgets.dart';
import '../../app/feature_module.dart';
import 'screens/rating_screen.dart';

/// Route names owned by the Ratings feature module.
class RatingRoutes {
  RatingRoutes._();

  static const String ratings = '/ratings';
  static const String history = '/ratings/history';
}

/// FeatureModule for Two-Way Ratings (Slot M6).
class RatingsFeatureModule extends FeatureModule {
  @override
  String get name => 'ratings';

  @override
  Map<String, WidgetBuilder> get routes => {
        RatingRoutes.ratings: (context) => const RatingScreen(),
        RatingRoutes.history: (context) => const RatingScreen(),
      };
}
