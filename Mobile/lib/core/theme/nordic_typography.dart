import 'package:flutter/material.dart';

import 'nordic_colors.dart';

/// Typography definitions for TỔ ẤM — Nordic Care design system.
/// Sizes, weights and line heights follow the DESIGN.md `typography` tokens
/// (.agents/skills/project-team-design-template/DESIGN.md). Headlines use
/// Newsreader, body, label and metric use Plus Jakarta Sans. Both families are
/// bundled under `assets/fonts/` (pubspec.yaml).
///
/// The `ui-templates` styles (MOB-UI-02) are at the end: Newsreader only for the
/// greeting and large numbers, everything else Plus Jakarta Sans.
class NordicTypography {
  NordicTypography._();

  /// Mobile redesign 2026-10-30: one family, Be Vietnam Pro (built for Vietnamese diacritics), for
  /// everything; `headlineFontFamily` is kept as a name so existing styles keep compiling.
  static const String fontFamily = 'Be Vietnam Pro';
  static const String headlineFontFamily = 'Be Vietnam Pro';

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

  /// `.greet`: the Home greeting.
  static const TextStyle greeting = TextStyle(
    fontFamily: headlineFontFamily,
    fontSize: 28.0,
    fontWeight: FontWeight.w700,
    height: 1.15,
    letterSpacing: -0.56,
    color: NordicColors.textTitle,
  );

  /// `.num.l`: the largest number of a screen (amount to pay, shift start).
  static const TextStyle numberLarge = TextStyle(
    fontFamily: headlineFontFamily,
    fontSize: 34.0,
    fontWeight: FontWeight.w700,
    height: 1.0,
    letterSpacing: -0.68,
    color: NordicColors.textTitle,
    fontFeatures: [FontFeature.tabularFigures()],
  );

  /// `.num.m`.
  static const TextStyle numberMedium = TextStyle(
    fontFamily: headlineFontFamily,
    fontSize: 26.0,
    fontWeight: FontWeight.w700,
    height: 1.0,
    letterSpacing: -0.52,
    color: NordicColors.textTitle,
    fontFeatures: [FontFeature.tabularFigures()],
  );

  /// `.num.s`: a number inside a tile.
  static const TextStyle numberSmall = TextStyle(
    fontFamily: headlineFontFamily,
    fontSize: 20.0,
    fontWeight: FontWeight.w700,
    height: 1.0,
    letterSpacing: -0.40,
    color: NordicColors.textTitle,
    fontFeatures: [FontFeature.tabularFigures()],
  );

  /// The largest title of a screen (MOB-UI-12, `apple-design` skill §15): tracking scales
  /// with size, so large text is tightened by -0.02 em (30 px x -0.02 = -0.6) and leading is
  /// tight (1.1). Body text keeps ~0 tracking and looser leading.
  static const TextStyle display = TextStyle(
    fontFamily: fontFamily,
    fontSize: 30.0,
    fontWeight: FontWeight.w700,
    height: 1.1,
    letterSpacing: -0.6,
    color: NordicColors.textTitle,
  );

  /// `.appbar.large .title`: the title of a tab screen.
  static const TextStyle screenTitle = TextStyle(
    fontFamily: fontFamily,
    fontSize: 26.0,
    fontWeight: FontWeight.w700,
    height: 1.2,
    letterSpacing: -0.5,
    color: NordicColors.textTitle,
  );

  /// `.appbar .title`: the title of a pushed screen.
  static const TextStyle appBarTitle = TextStyle(
    fontFamily: fontFamily,
    fontSize: 17.0,
    fontWeight: FontWeight.w600,
    height: 1.3,
    color: NordicColors.textTitle,
  );

  /// `.h2`.
  static const TextStyle titleMedium = TextStyle(
    fontFamily: fontFamily,
    fontSize: 18.0,
    fontWeight: FontWeight.w700,
    height: 1.3,
    color: NordicColors.textTitle,
  );

  /// `.h3`: card and section titles.
  static const TextStyle titleSmall = TextStyle(
    fontFamily: fontFamily,
    fontSize: 15.0,
    fontWeight: FontWeight.w600,
    height: 1.35,
    color: NordicColors.textTitle,
  );

  /// `.caps`: the small upper-case label above a number.
  static const TextStyle caps = TextStyle(
    fontFamily: fontFamily,
    fontSize: 11.0,
    fontWeight: FontWeight.w600,
    height: 1.3,
    letterSpacing: 0.66,
    color: NordicColors.textSecondary,
  );
}
