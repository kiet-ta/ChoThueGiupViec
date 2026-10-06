import 'package:flutter/material.dart';
import '../core/theme/nordic_theme.dart';
import '../features/home/home_screen.dart';
import 'routes.dart';

/// Root application widget for TỔ ẤM — Nordic Care.
class ToAmApp extends StatelessWidget {
  const ToAmApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'TỔ ẤM — Nordic Care',
      debugShowCheckedModeBanner: false,
      theme: NordicTheme.lightTheme,
      home: const HomeScreen(),
      routes: AppRoutes.routes,
      onGenerateRoute: AppRoutes.onGenerateRoute,
    );
  }
}
