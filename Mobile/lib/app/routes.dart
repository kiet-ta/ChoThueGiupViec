import 'package:flutter/widgets.dart';
import 'feature_registry.dart';

/// Central route names and routing table delegate for the Mobile application.
class AppRoutes {
  AppRoutes._();

  // Root & Navigation
  static const String home = '/';
  static const String customerHome = '/customer';
  static const String workerHome = '/worker';

  // Identity & Auth (M1)
  static const String login = '/login';
  static const String phoneInput = '/auth/phone-input';
  static const String otpVerify = '/auth/otp-verify';

  // Customers (M1)
  static const String addressList = '/addresses';
  static const String addressForm = '/addresses/form';
  static const String customerProfile = '/customer/profile';
  static const String favoriteWorkers = '/customer/favorites';

  // Booking & Payments (M2)
  static const String booking = '/booking';
  static const String bookingDetail = '/booking/detail';
  static const String payments = '/payments';
  static const String paymentsMomo = '/payments/momo';

  // Dispatch (M3)
  static const String dispatchOffers = '/dispatch/offers';
  static const String dispatchCheckIn = '/dispatch/check-in';

  // Workers (M4)
  static const String workersProfile = '/workers/profile';
  static const String workersEkyc = '/workers/ekyc';
  static const String workersSlots = '/workers/slots';

  // Agencies (M5)
  static const String agenciesRoster = '/agencies/roster';
  static const String agenciesProfile = '/agencies/profile';

  // Ratings, Disputes, Payouts (M6)
  static const String ratings = '/ratings';
  static const String ratingsHistory = '/ratings/history';
  static const String disputes = '/disputes';
  static const String disputesCreate = '/disputes/create';
  static const String payouts = '/payouts';
  static const String payoutsHistory = '/payouts/history';

  /// Delegated routes table aggregating all registered feature modules.
  static Map<String, WidgetBuilder> get routes => FeatureRegistry.routes;

  /// Delegated composite route factory for dynamic / parameterized routes.
  static Route<dynamic>? onGenerateRoute(RouteSettings settings) =>
      FeatureRegistry.onGenerateRoute(settings);
}
