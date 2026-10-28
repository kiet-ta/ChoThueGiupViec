import 'package:flutter/material.dart';

/// Semantic color tokens for the TỔ ẤM — Nordic Care design system.
/// DESIGN.md is the single source of truth (.agents/skills/project-team-design-template/DESIGN.md).
/// A constant marked "no DESIGN.md token" has no YAML token yet; it awaits a leader decision (issue #61).
class NordicColors {
  NordicColors._();

  // Canvas and Surfaces
  static const Color canvas = Color(0xFFFAF8F3); // canvas-background
  static const Color surface = Color(0xFFFFFFFF); // card-surface
  static const Color surfaceSubtle = Color(0xFFF4F2EB); // no DESIGN.md token
  static const Color surfaceContainer = Color(0xFFF0EEE9); // surface-container
  static const Color surfaceDim = Color(0xFFDBDAD5); // surface-dim
  static const Color surfaceLow = Color(0xFFF5F3EE); // surface-container-low

  // Brand Palette: the Mobile palette #2F6B3F / #7FB77E / #F7C85C / #FFF6C0 (MOB-UI-09, owner
  // decision 2026-10-28, Mobile only: these greens and ambers deliberately differ from
  // DESIGN.md). A "derived" value is a palette colour shifted until the contrast it needs holds.
  static const Color primary = Color(0xFF2F6B3F); // palette; white text 6.4:1
  // Derived: #2F6B3F darker, the pressed state.
  static const Color primaryHover = Color(0xFF285C36);
  static const Color primaryContainer = Color(0xFF7FB77E); // palette
  // Derived: #7FB77E darker until white text on it reaches 4.5:1 (snackbar background).
  static const Color secondaryAccent = Color(0xFF467F49);
  // Derived: #7FB77E tinted with white until #2F6B3F text on it reaches 4.5:1 (ok pill).
  static const Color primaryFixed = Color(0xFFD2E6D2);
  static const Color accentAmber = Color(0xFFF7C85C); // palette
  static const Color amberSoft = Color(0xFFFFF6C0); // palette
  // on-secondary-fixed-variant: text on amberSoft.
  static const Color onAmberSoft = Color(0xFF5F4100);
  static const Color contrastDark = Color(0xFF1A2F25); // no DESIGN.md token
  static const Color contrastCard = Color(0xFF223B2F); // no DESIGN.md token

  // Typography & Content
  static const Color textTitle = Color(0xFF2F3A34); // text-primary
  static const Color textBody = Color(0xFF4B5563); // no DESIGN.md token
  static const Color textSecondary = Color(0xFF6B7280); // text-secondary
  static const Color textInverse = Color(0xFFFFFFFF); // on-primary

  // Borders & Accents
  static const Color border = Color(0xFFE5E7EB); // no DESIGN.md token
  static const Color borderVariant = Color(0xFFC2C8BF); // outline-variant
  static const Color error = Color(0xFFBA1A1A); // error
  static const Color errorContainer = Color(0xFFFFDAD6); // error-container
  static const Color success = Color(0xFF2E7D32); // no DESIGN.md token
}
