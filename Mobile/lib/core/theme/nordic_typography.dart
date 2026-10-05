import 'package:flutter/material.dart';
import 'nordic_colors.dart';

/// Typography definitions for TỔ ẤM — Nordic Care design system.
/// Uses Plus Jakarta Sans font style for clean readability.
class NordicTypography {
  NordicTypography._();

  static const String fontFamily = 'Plus Jakarta Sans';

  static const TextStyle h1 = TextStyle(
    fontSize: 28.0,
    fontWeight: FontWeight.w700,
    height: 1.25,
    letterSpacing: -0.5,
    color: NordicColors.textTitle,
  );

  static const TextStyle h2 = TextStyle(
    fontSize: 22.0,
    fontWeight: FontWeight.w700,
    height: 1.3,
    letterSpacing: -0.3,
    color: NordicColors.textTitle,
  );

  static const TextStyle h3 = TextStyle(
    fontSize: 18.0,
    fontWeight: FontWeight.w600,
    height: 1.35,
    color: NordicColors.textTitle,
  );

  static const TextStyle bodyLarge = TextStyle(
    fontSize: 16.0,
    fontWeight: FontWeight.w400,
    height: 1.5,
    color: NordicColors.textBody,
  );

  static const TextStyle bodyRegular = TextStyle(
    fontSize: 14.0,
    fontWeight: FontWeight.w400,
    height: 1.5,
    color: NordicColors.textBody,
  );

  static const TextStyle bodySmall = TextStyle(
    fontSize: 12.0,
    fontWeight: FontWeight.w400,
    height: 1.4,
    color: NordicColors.textSecondary,
  );

  static const TextStyle labelMedium = TextStyle(
    fontSize: 13.0,
    fontWeight: FontWeight.w600,
    height: 1.4,
    color: NordicColors.textTitle,
  );

  static const TextStyle labelSmall = TextStyle(
    fontSize: 11.0,
    fontWeight: FontWeight.w600,
    height: 1.3,
    letterSpacing: 0.5,
    color: NordicColors.primary,
  );

  static const TextStyle priceHighlight = TextStyle(
    fontSize: 22.0,
    fontWeight: FontWeight.w800,
    height: 1.2,
    letterSpacing: -0.5,
    color: NordicColors.primary,
  );
}
