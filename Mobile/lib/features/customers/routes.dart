import 'package:flutter/material.dart';
import '../../app/feature_module.dart';
import 'models/customer_address.dart';
import 'screens/address_form_screen.dart';
import 'screens/address_list_screen.dart';
import 'screens/customer_profile_screen.dart';
import 'screens/favorite_workers_screen.dart';

/// Route names owned by the Customers feature module.
class CustomerRoutes {
  CustomerRoutes._();

  static const String addressList = '/addresses';
  static const String addressForm = '/addresses/form';
  static const String customerProfile = '/customer/profile';
  static const String favoriteWorkers = '/customer/favorites';
}

/// FeatureModule for Customer Profile & Address Book (Slot M1).
class CustomersFeatureModule extends FeatureModule {
  @override
  String get name => 'customers';

  @override
  Map<String, WidgetBuilder> get routes => {
        CustomerRoutes.addressList: (context) => const AddressListScreen(),
        CustomerRoutes.addressForm: (context) => const AddressFormScreen(),
        CustomerRoutes.customerProfile: (context) =>
            const CustomerProfileScreen(),
        CustomerRoutes.favoriteWorkers: (context) =>
            const FavoriteWorkersScreen(),
      };

  @override
  Route<dynamic>? onGenerateRoute(RouteSettings settings) {
    if (settings.name == CustomerRoutes.addressForm &&
        settings.arguments is CustomerAddress) {
      return MaterialPageRoute(
        settings: settings,
        builder: (context) => AddressFormScreen(
          initialAddress: settings.arguments as CustomerAddress,
        ),
      );
    }
    return null;
  }
}
