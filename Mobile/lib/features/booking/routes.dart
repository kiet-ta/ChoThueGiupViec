import 'package:flutter/widgets.dart';
import '../../app/feature_module.dart';
import 'screens/booking_screen.dart';

/// Route names owned by the Booking feature module.
class BookingRoutes {
  BookingRoutes._();

  static const String booking = '/booking';
  static const String bookingDetail = '/booking/detail';
}

/// FeatureModule for Booking (Slot M2).
class BookingFeatureModule extends FeatureModule {
  @override
  String get name => 'booking';

  @override
  Map<String, WidgetBuilder> get routes => {
        BookingRoutes.booking: (context) => const BookingScreen(),
        BookingRoutes.bookingDetail: (context) => const BookingScreen(),
      };
}
