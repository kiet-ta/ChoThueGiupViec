import 'package:flutter/material.dart';

import 'nordic_colors.dart';
import 'nordic_radius.dart';
import 'nordic_typography.dart';

/// Complete ThemeData setup for TỔ ẤM — Nordic Care.
class NordicTheme {
  NordicTheme._();

  static ThemeData get lightTheme {
    return ThemeData(
      useMaterial3: true,
      fontFamily: NordicTypography.fontFamily,
      scaffoldBackgroundColor: NordicColors.canvas,
      colorScheme: const ColorScheme(
        brightness: Brightness.light,
        primary: NordicColors.primary,
        onPrimary: Colors.white,
        primaryContainer: NordicColors.primaryFixed,
        onPrimaryContainer: NordicColors.primary,
        secondary: NordicColors.secondaryAccent,
        onSecondary: Colors.white,
        error: NordicColors.error,
        onError: Colors.white,
        errorContainer: NordicColors.errorContainer,
        surface: NordicColors.surface,
        onSurface: NordicColors.textTitle,
        onSurfaceVariant: NordicColors.textSecondary,
        surfaceContainerLow: NordicColors.surfaceLow,
        surfaceContainer: NordicColors.surfaceContainer,
        surfaceContainerHighest: NordicColors.surfaceContainer,
        outline: NordicColors.border,
        outlineVariant: NordicColors.borderVariant,
      ),
      // ui-templates `.appbar`: flat, on the canvas, 17 px centred-weight title.
      appBarTheme: const AppBarTheme(
        backgroundColor: NordicColors.canvas,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        centerTitle: true,
        scrolledUnderElevation: 0,
        iconTheme: IconThemeData(color: NordicColors.textTitle),
        titleTextStyle: NordicTypography.appBarTitle,
      ),
      // ui-templates `.bnav`: white bar, hairline top border, primary for the selected item.
      navigationBarTheme: NavigationBarThemeData(
        backgroundColor: NordicColors.surface,
        surfaceTintColor: Colors.transparent,
        indicatorColor: NordicColors.primaryFixed,
        elevation: 0,
        height: 68,
        labelTextStyle: WidgetStateProperty.resolveWith(
          (states) => TextStyle(
            fontFamily: NordicTypography.fontFamily,
            fontSize: 11,
            fontWeight: states.contains(WidgetState.selected)
                ? FontWeight.w700
                : FontWeight.w500,
            color: states.contains(WidgetState.selected)
                ? NordicColors.primary
                : NordicColors.textSecondary,
          ),
        ),
        iconTheme: WidgetStateProperty.resolveWith(
          (states) => IconThemeData(
            size: 24,
            color: states.contains(WidgetState.selected)
                ? NordicColors.primary
                : NordicColors.textSecondary,
          ),
        ),
      ),
      floatingActionButtonTheme: const FloatingActionButtonThemeData(
        backgroundColor: NordicColors.primary,
        foregroundColor: Colors.white,
        elevation: 4,
        shape: CircleBorder(),
      ),
      // ui-templates `.chip`: button corners, hairline border, primary fill when selected.
      chipTheme: ChipThemeData(
        backgroundColor: NordicColors.surface,
        selectedColor: NordicColors.primary,
        secondarySelectedColor: NordicColors.primary,
        checkmarkColor: Colors.white,
        side: const BorderSide(color: NordicColors.border),
        shape: NordicRadius.buttonShape,
        labelStyle: const TextStyle(
          fontFamily: NordicTypography.fontFamily,
          fontSize: 13,
          fontWeight: FontWeight.w500,
          color: NordicColors.textTitle,
        ),
        secondaryLabelStyle: const TextStyle(
          fontFamily: NordicTypography.fontFamily,
          fontSize: 13,
          fontWeight: FontWeight.w600,
          color: Colors.white,
        ),
        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
      ),
      // ui-templates `.seg`: a track with button corners, the selected segment white.
      segmentedButtonTheme: SegmentedButtonThemeData(
        style: ButtonStyle(
          backgroundColor: WidgetStateProperty.resolveWith(
            (states) => states.contains(WidgetState.selected)
                ? NordicColors.surface
                : NordicColors.surfaceContainer,
          ),
          foregroundColor: WidgetStateProperty.resolveWith(
            (states) => states.contains(WidgetState.selected)
                ? NordicColors.textTitle
                : NordicColors.textSecondary,
          ),
          side: const WidgetStatePropertyAll(
            BorderSide(color: NordicColors.surfaceContainer, width: 2),
          ),
          shape: const WidgetStatePropertyAll(NordicRadius.buttonShape),
          textStyle: const WidgetStatePropertyAll(
            TextStyle(
              fontFamily: NordicTypography.fontFamily,
              fontSize: 13,
              fontWeight: FontWeight.w600,
            ),
          ),
        ),
      ),
      bottomSheetTheme: const BottomSheetThemeData(
        backgroundColor: NordicColors.surface,
        surfaceTintColor: Colors.transparent,
        showDragHandle: true,
        shape: RoundedRectangleBorder(borderRadius: NordicRadius.none),
      ),
      listTileTheme: const ListTileThemeData(
        iconColor: NordicColors.textSecondary,
        titleTextStyle: TextStyle(
          fontFamily: NordicTypography.fontFamily,
          fontSize: 15,
          fontWeight: FontWeight.w500,
          color: NordicColors.textTitle,
        ),
        subtitleTextStyle: TextStyle(
          fontFamily: NordicTypography.fontFamily,
          fontSize: 12,
          color: NordicColors.textSecondary,
        ),
      ),
      // Dialogs are square like cards (Material 3 rounds them by 28 px).
      dialogTheme: const DialogThemeData(
        shape: RoundedRectangleBorder(borderRadius: NordicRadius.none),
      ),
      cardTheme: CardThemeData(
        color: NordicColors.surface,
        elevation: 0,
        shape: RoundedRectangleBorder(
          borderRadius: NordicRadius.none,
          side: const BorderSide(color: NordicColors.border, width: 1.0),
        ),
      ),
      elevatedButtonTheme: ElevatedButtonThemeData(
        style: ElevatedButton.styleFrom(
          backgroundColor: NordicColors.primary,
          foregroundColor: Colors.white,
          elevation: 0,
          disabledBackgroundColor: NordicColors.surfaceDim,
          disabledForegroundColor: NordicColors.textSecondary,
          textStyle: const TextStyle(
            fontFamily: NordicTypography.fontFamily,
            fontSize: 15.0,
            fontWeight: FontWeight.w600,
          ),
          minimumSize: const Size(64, 52),
          shape: NordicRadius.buttonShape,
          padding: const EdgeInsets.symmetric(horizontal: 24.0, vertical: 14.0),
        ),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          backgroundColor: NordicColors.primary,
          foregroundColor: Colors.white,
          disabledBackgroundColor: NordicColors.surfaceDim,
          disabledForegroundColor: NordicColors.textSecondary,
          textStyle: const TextStyle(
            fontFamily: NordicTypography.fontFamily,
            fontSize: 15.0,
            fontWeight: FontWeight.w600,
          ),
          minimumSize: const Size(64, 52),
          shape: NordicRadius.buttonShape,
        ),
      ),
      textButtonTheme: TextButtonThemeData(
        style: TextButton.styleFrom(
          foregroundColor: NordicColors.primary,
          shape: NordicRadius.buttonShape,
          textStyle: const TextStyle(
            fontFamily: NordicTypography.fontFamily,
            fontSize: 13.0,
            fontWeight: FontWeight.w600,
          ),
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          foregroundColor: NordicColors.textTitle,
          side: const BorderSide(color: NordicColors.border, width: 1.0),
          backgroundColor: NordicColors.surface,
          minimumSize: const Size(64, 52),
          shape: NordicRadius.buttonShape,
          padding: const EdgeInsets.symmetric(horizontal: 24.0, vertical: 14.0),
          textStyle: const TextStyle(
            fontFamily: NordicTypography.fontFamily,
            fontSize: 15.0,
            fontWeight: FontWeight.w600,
          ),
        ),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: NordicColors.surface,
        contentPadding: const EdgeInsets.symmetric(
          horizontal: 16.0,
          vertical: 14.0,
        ),
        border: OutlineInputBorder(
          borderRadius: NordicRadius.none,
          borderSide: const BorderSide(color: NordicColors.border, width: 1.0),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: NordicRadius.none,
          borderSide: const BorderSide(color: NordicColors.border, width: 1.0),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: NordicRadius.none,
          borderSide: const BorderSide(color: NordicColors.primary, width: 1.5),
        ),
        errorBorder: OutlineInputBorder(
          borderRadius: NordicRadius.none,
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
