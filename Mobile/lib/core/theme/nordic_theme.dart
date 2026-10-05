import 'package:flutter/material.dart';
import 'nordic_colors.dart';
import 'nordic_typography.dart';

/// Complete ThemeData setup for TỔ ẤM — Nordic Care.
class NordicTheme {
  NordicTheme._();

  static ThemeData get lightTheme {
    return ThemeData(
      useMaterial3: true,
      scaffoldBackgroundColor: NordicColors.canvas,
      colorScheme: const ColorScheme(
        brightness: Brightness.light,
        primary: NordicColors.primary,
        onPrimary: Colors.white,
        secondary: NordicColors.secondaryAccent,
        onSecondary: Colors.white,
        error: NordicColors.error,
        onError: Colors.white,
        surface: NordicColors.surface,
        onSurface: NordicColors.textTitle,
      ),
      appBarTheme: const AppBarTheme(
        backgroundColor: Colors.transparent,
        elevation: 0,
        centerTitle: false,
        scrolledUnderElevation: 0,
        iconTheme: IconThemeData(color: NordicColors.textTitle),
        titleTextStyle: NordicTypography.h2,
      ),
      cardTheme: CardThemeData(
        color: NordicColors.surface,
        elevation: 0,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(16.0),
          side: const BorderSide(color: NordicColors.border, width: 1.0),
        ),
      ),
      elevatedButtonTheme: ElevatedButtonThemeData(
        style: ElevatedButton.styleFrom(
          backgroundColor: NordicColors.primary,
          foregroundColor: Colors.white,
          elevation: 0,
          textStyle: const TextStyle(
            fontSize: 14.0,
            fontWeight: FontWeight.w600,
          ),
          shape: const StadiumBorder(),
          padding: const EdgeInsets.symmetric(horizontal: 24.0, vertical: 14.0),
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          foregroundColor: NordicColors.textTitle,
          side: const BorderSide(color: NordicColors.border, width: 1.0),
          shape: const StadiumBorder(),
          padding: const EdgeInsets.symmetric(horizontal: 24.0, vertical: 14.0),
          textStyle: const TextStyle(
            fontSize: 14.0,
            fontWeight: FontWeight.w500,
          ),
        ),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: NordicColors.surface,
        contentPadding: const EdgeInsets.symmetric(horizontal: 16.0, vertical: 14.0),
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(12.0),
          borderSide: const BorderSide(color: NordicColors.border, width: 1.0),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(12.0),
          borderSide: const BorderSide(color: NordicColors.border, width: 1.0),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(12.0),
          borderSide: const BorderSide(color: NordicColors.primary, width: 1.5),
        ),
        errorBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(12.0),
          borderSide: const BorderSide(color: NordicColors.error, width: 1.0),
        ),
        hintStyle: NordicTypography.bodyRegular.copyWith(
          color: NordicColors.textSecondary,
        ),
      ),
      dividerTheme: const DividerThemeData(
        color: NordicColors.border,
        thickness: 1.0,
        space: 24.0,
      ),
    );
  }
}
