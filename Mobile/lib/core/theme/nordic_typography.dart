import 'package:flutter/material.dart';

import 'nordic_colors.dart';

/// Typography definitions for TỔ ẤM — Nordic Care design system.
/// Sizes, weights and line heights follow the DESIGN.md `typography` tokens
/// (.agents/skills/project-team-design-template/DESIGN.md). Headlines use
/// Newsreader, body, label and metric use Plus Jakarta Sans. No font file is
/// bundled yet, so an unregistered family resolves to the platform default.
class NordicTypography {
  NordicTypography._();

  static const String fontFamily = 'Plus Jakarta Sans';
  static const String headlineFontFamily = 'Newsreader';

  /// DESIGN.md headline-lg (40 px).
  static const TextStyle headlineLg = TextStyle(
    fontFamily: headlineFontFamily,
    fontSize: 40.0,
    fontWeight: FontWeight.w600,
    height: 1.2,
    color: NordicColors.textTitle,
  );

  /// DESIGN.md headline-lg-mobile (32 px).
  static const TextStyle headlineLgMobile = TextStyle(
    fontFamily: headlineFontFamily,
    fontSize: 32.0,
    fontWeight: FontWeight.w600,
    height: 1.25,
    color: NordicColors.textTitle,
  );

  /// DESIGN.md headline-md.
  static const TextStyle h1 = TextStyle(
    fontFamily: headlineFontFamily,
    fontSize: 28.0,
    fontWeight: FontWeight.w500,
    height: 1.3,
    color: NordicColors.textTitle,
  );

  /// DESIGN.md headline-sm.
  static const TextStyle h2 = TextStyle(
    fontFamily: headlineFontFamily,
    fontSize: 22.0,
    fontWeight: FontWeight.w500,
    height: 1.4,
    color: NordicColors.textTitle,
  );

  /// DESIGN.md body-lg (18 px); the closest token for a 18 px sub-heading.
  static const TextStyle h3 = TextStyle(
    fontFamily: fontFamily,
    fontSize: 18.0,
    fontWeight: FontWeight.w400,
    height: 1.6,
    color: NordicColors.textTitle,
  );

  /// DESIGN.md body-md.
  static const TextStyle bodyLarge = TextStyle(
    fontFamily: fontFamily,
    fontSize: 16.0,
    fontWeight: FontWeight.w400,
    height: 1.5,
    color: NordicColors.textBody,
  );

  /// DESIGN.md body-sm.
  static const TextStyle bodyRegular = TextStyle(
    fontFamily: fontFamily,
    fontSize: 14.0,
    fontWeight: FontWeight.w400,
    height: 1.5,
    color: NordicColors.textBody,
  );

  /// DESIGN.md label-sm (no 12 px body token exists).
  static const TextStyle bodySmall = TextStyle(
    fontFamily: fontFamily,
    fontSize: 12.0,
    fontWeight: FontWeight.w500,
    height: 1.4,
    color: NordicColors.textSecondary,
  );

  /// DESIGN.md label-md.
  static const TextStyle labelMedium = TextStyle(
    fontFamily: fontFamily,
    fontSize: 14.0,
    fontWeight: FontWeight.w500,
    height: 1.4,
    color: NordicColors.textTitle,
  );

  /// DESIGN.md label-sm.
  static const TextStyle labelSmall = TextStyle(
    fontFamily: fontFamily,
    fontSize: 12.0,
    fontWeight: FontWeight.w500,
    height: 1.4,
    color: NordicColors.primary,
  );

  /// DESIGN.md metric (prices, numbers).
  static const TextStyle priceHighlight = TextStyle(
    fontFamily: fontFamily,
    fontSize: 24.0,
    fontWeight: FontWeight.w600,
    height: 1.2,
    color: NordicColors.primary,
  );
}
