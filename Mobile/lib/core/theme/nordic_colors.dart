import 'package:flutter/material.dart';

/// Semantic color tokens for the TỔ ẤM — Nordic Care design system.
/// DESIGN.md is the single source of truth (.agents/skills/project-team-design-template/DESIGN.md).
/// A constant marked "no DESIGN.md token" has no YAML token yet; it awaits a leader decision (issue #61).
///
/// Mobile redesign 2026-10-30 (owner: Duy): the Mobile app moves to a deep-green, cool-neutral
/// palette with depth (`Mobile/DESIGN-SYSTEM.md` §3.1). Mobile only: DESIGN.md and the Web keep their values.
class NordicColors {
  NordicColors._();

  // Canvas and Surfaces
  static const Color canvas = Color(0xFFEEF1EC); // Mobile canvas (cool neutral)
  static const Color surface = Color(0xFFFFFFFF); // card-surface
  static const Color surfaceSubtle = Color(0xFFF4F6F2); // no DESIGN.md token
  static const Color surfaceContainer = Color(
    0xFFE6EBE7,
  ); // Mobile surface-container
  static const Color surfaceDim = Color(0xFFD5DCD7); // Mobile surface-dim
  static const Color surfaceLow = Color(
    0xFFF3F5F1,
  ); // Mobile surface-container-low

  // Brand palette (Mobile redesign 2026-10-30, owner: Duy; Mobile only, deliberately different
  // from DESIGN.md): deep green #0F3D2E, accent green #1F7A55, soft green #DCEDE3, amber #F2B544.
  static const Color primary = Color(0xFF0F3D2E); // deep green; white text 12:1
  // Derived: #0F3D2E darker, the pressed state.
  static const Color primaryHover = Color(0xFF0A2E22);
  // Accent green: selection rings, check badges, small accents; white text on it 5.3:1.
  static const Color primaryContainer = Color(0xFF1F7A55);
  // Accent green for icons and snackbars with white text (5.3:1).
  static const Color secondaryAccent = Color(0xFF1F7A55);
  // Soft green: ok pills, selected tab pill, tinted cards; primary text on it 10:1.
  static const Color primaryFixed = Color(0xFFDCEDE3);
  static const Color accentAmber = Color(
    0xFFF2B544,
  ); // stars, dots; dark text only
  static const Color amberSoft = Color(0xFFFFF6C0); // palette
  // on-secondary-fixed-variant: text on amberSoft.
  static const Color onAmberSoft = Color(0xFF5F4100);
  static const Color contrastDark = Color(
    0xFF0F3D2E,
  ); // dark header, gradients over photos
  static const Color contrastCard = Color(0xFF17573D); // dark cards

  // Typography & Content
  static const Color textTitle = Color(0xFF101814); // Mobile text-primary
  static const Color textBody = Color(0xFF34433B); // no DESIGN.md token
  static const Color textSecondary = Color(
    0xFF5E6A63,
  ); // Mobile text-secondary; 4.8:1 on canvas
  static const Color textInverse = Color(0xFFFFFFFF); // on-primary
  // Secondary text on the dark header / dark cards (6.6:1 on contrastDark).
  static const Color onDarkMuted = Color(0xFFA9C7B8);

  // Borders & Accents
  static const Color border = Color(0xFFDDE3DF); // no DESIGN.md token
  static const Color borderVariant = Color(
    0xFFCBD5CF,
  ); // Mobile outline-variant
  static const Color error = Color(0xFFBA1A1A); // error
  static const Color errorContainer = Color(0xFFFFDAD6); // error-container
  static const Color success = Color(0xFF2E7D32); // no DESIGN.md token
}
