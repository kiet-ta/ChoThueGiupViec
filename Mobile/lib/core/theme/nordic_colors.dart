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

  // Brand Palette
  static const Color primary = Color(0xFF416448); // primary
  static const Color primaryHover = Color(0xFF56705B); // primary-hover
  static const Color primaryContainer = Color(0xFF597D60); // primary-container
  static const Color secondaryAccent = Color(0xFF6B8F71); // no DESIGN.md token
  static const Color primaryFixed = Color(0xFFC5ECC9); // primary-fixed
  static const Color accentAmber = Color(0xFFD9A441); // Warm Amber (prose)
  static const Color amberSoft = Color(0xFFFFDEAA); // secondary-fixed
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
