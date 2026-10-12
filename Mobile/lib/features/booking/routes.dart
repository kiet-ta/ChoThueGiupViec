import 'package:flutter/widgets.dart';
import '../../app/feature_module.dart';
import 'screens/booking_screen.dart';
import 'screens/order_detail_screen.dart';
import 'screens/order_history_screen.dart';

/// Route names owned by the Booking feature module.
class BookingRoutes {
  BookingRoutes._();

  static const String booking = '/booking';

  /// One order (tracking, cancel, "Làm lần 2"); arguments: `OrderDetailArgs`.
  static const String bookingDetail = '/booking/detail';
  static const String orders = '/booking/orders';
}

/// FeatureModule for Booking (Slot M2).
class BookingFeatureModule extends FeatureModule {
  @override
  String get name => 'booking';

  @override
  Map<String, WidgetBuilder> get routes => {
        BookingRoutes.booking: (context) => const BookingScreen(),
        BookingRoutes.bookingDetail: (context) => const OrderDetailScreen(),
        BookingRoutes.orders: (context) => const OrderHistoryScreen(),
      };
}
