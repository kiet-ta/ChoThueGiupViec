import 'package:flutter/material.dart';
import '../../app/feature_module.dart';
import '../../core/models/user_role.dart';
import 'screens/otp_verify_screen.dart';
import 'screens/phone_input_screen.dart';

/// Route names owned by the Identity feature module.
class IdentityRoutes {
  IdentityRoutes._();

  static const String phoneInput = '/auth/phone-input';
  static const String otpVerify = '/auth/otp-verify';
  static const String login = '/login';
}

/// FeatureModule for Identity & Authentication (Slot M1).
class IdentityFeatureModule extends FeatureModule {
  @override
  String get name => 'identity';

  @override
  Map<String, WidgetBuilder> get routes => {
        IdentityRoutes.phoneInput: (context) => const PhoneInputScreen(),
        IdentityRoutes.login: (context) => const PhoneInputScreen(),
      };

  @override
  Route<dynamic>? onGenerateRoute(RouteSettings settings) {
    if (settings.name == IdentityRoutes.otpVerify) {
      final args = settings.arguments;
      if (args is Map<String, dynamic>) {
        return MaterialPageRoute(
          settings: settings,
          builder: (context) => OtpVerifyScreen(
            phoneNumber: args['phoneNumber'] as String? ?? '',
            role: args['role'] as AppRole? ?? AppRole.customer,
            resendCooldownSeconds:
                args['resendCooldownSeconds'] as int? ?? 60,
          ),
        );
      }
      return MaterialPageRoute(
        settings: settings,
        builder: (context) => const OtpVerifyScreen(phoneNumber: ''),
      );
    }
    return null;
  }
}
