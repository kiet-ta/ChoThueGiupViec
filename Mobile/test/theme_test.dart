import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/theme/nordic_colors.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/core/theme/nordic_typography.dart';

/// Every NordicColors constant that has a DESIGN.md YAML token, as
/// `YAML token name -> (constant, DESIGN.md hex)`.
/// The brand greens, the canvas, the surfaces, the text greys and the outlines are left out on
/// purpose: the Mobile app uses its own palette for them (MOB-UI-09, then the Mobile redesign of
/// 2026-10-30, owner: Duy; see [_mobilePalette]).
const Map<String, (Color, String)> _tokenColors = {
  'card-surface': (NordicColors.surface, 'FFFFFF'),
  'on-primary': (NordicColors.textInverse, 'FFFFFF'),
  'error': (NordicColors.error, 'BA1A1A'),
  'error-container': (NordicColors.errorContainer, 'FFDAD6'),
};

/// The Mobile palette (redesign 2026-10-30: deep green #0F3D2E, accent #1F7A55, cool neutrals).
const Map<String, (Color, int)> _mobilePalette = {
  'primary': (NordicColors.primary, 0xFF0F3D2E),
  'primaryHover': (NordicColors.primaryHover, 0xFF0A2E22),
  'primaryContainer': (NordicColors.primaryContainer, 0xFF1F7A55),
  'secondaryAccent': (NordicColors.secondaryAccent, 0xFF1F7A55),
  'primaryFixed': (NordicColors.primaryFixed, 0xFFDCEDE3),
  'accentAmber': (NordicColors.accentAmber, 0xFFF2B544),
  'amberSoft': (NordicColors.amberSoft, 0xFFFFF6C0),
  'canvas': (NordicColors.canvas, 0xFFEEF1EC),
  'surfaceContainer': (NordicColors.surfaceContainer, 0xFFE6EBE7),
  'surfaceDim': (NordicColors.surfaceDim, 0xFFD5DCD7),
  'textTitle': (NordicColors.textTitle, 0xFF101814),
  'textSecondary': (NordicColors.textSecondary, 0xFF5E6A63),
  'borderVariant': (NordicColors.borderVariant, 0xFFCBD5CF),
  'contrastDark': (NordicColors.contrastDark, 0xFF0F3D2E),
  'onDarkMuted': (NordicColors.onDarkMuted, 0xFFA9C7B8),
};

/// WCAG 2 contrast ratio of two opaque colours.
double _contrast(Color a, Color b) {
  final la = a.computeLuminance();
  final lb = b.computeLuminance();
  final (light, dark) = la > lb ? (la, lb) : (lb, la);
  return (light + 0.05) / (dark + 0.05);
}

const String _designMdPath =
    '../.agents/skills/project-team-design-template/DESIGN.md';

void main() {
  group('NordicColors & NordicTheme Tests', () {
    test('every color constant with a DESIGN.md token equals that token', () {
      _tokenColors.forEach((token, entry) {
        final (color, hex) = entry;
        expect(
          color,
          Color(int.parse('FF$hex', radix: 16)),
          reason: 'DESIGN.md token $token',
        );
      });
    });

    test('token values match the YAML in DESIGN.md', () {
      final file = File(_designMdPath);
      if (!file.existsSync()) {
        markTestSkipped('DESIGN.md not found at $_designMdPath');
        return;
      }
      final yaml = file.readAsStringSync();
      _tokenColors.forEach((token, entry) {
        final pattern = RegExp(
          '^  ${RegExp.escape(token)}: \'#([0-9a-fA-F]{6})\'',
          multiLine: true,
        );
        final match = pattern.firstMatch(yaml);
        expect(match, isNotNull, reason: 'token $token not found in DESIGN.md');
        expect(
          match!.group(1)!.toUpperCase(),
          entry.$2,
          reason: 'DESIGN.md token $token',
        );
      });
    });

    test('the brand colours follow the Mobile palette', () {
      _mobilePalette.forEach((name, entry) {
        expect(entry.$1, Color(entry.$2), reason: 'NordicColors.$name');
      });
    });

    test('the palette pairs in use stay readable (WCAG AA 4.5:1)', () {
      const white = NordicColors.textInverse;
      // Buttons, cards and the snackbar carry white text.
      expect(_contrast(white, NordicColors.primary), greaterThanOrEqualTo(4.5));
      expect(
        _contrast(white, NordicColors.primaryHover),
        greaterThanOrEqualTo(4.5),
      );
      expect(
        _contrast(white, NordicColors.secondaryAccent),
        greaterThanOrEqualTo(4.5),
      );
      // The ok pill: primary text on primaryFixed; the warn pill: onAmberSoft on amberSoft.
      expect(
        _contrast(NordicColors.primary, NordicColors.primaryFixed),
        greaterThanOrEqualTo(4.5),
      );
      expect(
        _contrast(NordicColors.onAmberSoft, NordicColors.amberSoft),
        greaterThanOrEqualTo(4.5),
      );
      // Secondary text on the canvas and on the dark header; accent text on white.
      expect(
        _contrast(NordicColors.textSecondary, NordicColors.canvas),
        greaterThanOrEqualTo(4.5),
      );
      expect(
        _contrast(NordicColors.onDarkMuted, NordicColors.contrastDark),
        greaterThanOrEqualTo(4.5),
      );
      expect(
        _contrast(NordicColors.primaryContainer, NordicColors.surface),
        greaterThanOrEqualTo(4.5),
      );
    });

    test('NordicTheme configured with lightTheme properties', () {
      final theme = NordicTheme.lightTheme;
      expect(theme.useMaterial3, isTrue);
      expect(theme.scaffoldBackgroundColor, NordicColors.canvas);
      expect(theme.colorScheme.primary, NordicColors.primary);
      expect(theme.colorScheme.secondary, NordicColors.secondaryAccent);
    });
  });

  group('NordicTypography follows the DESIGN.md typography tokens', () {
    void expectStyle(
      TextStyle style,
      String family,
      double size,
      FontWeight weight,
      double height,
    ) {
      expect(style.fontFamily, family);
      expect(style.fontSize, size);
      expect(style.fontWeight, weight);
      expect(style.height, height);
    }

    test(
      'headlines use Be Vietnam Pro (one family since the 2026-10-30 redesign)',
      () {
        expectStyle(
          NordicTypography.headlineLg,
          'Be Vietnam Pro',
          40,
          FontWeight.w600,
          1.2,
        );
        expectStyle(
          NordicTypography.headlineLgMobile,
          'Be Vietnam Pro',
          32,
          FontWeight.w600,
          1.25,
        );
        expectStyle(
          NordicTypography.h1,
          'Be Vietnam Pro',
          28,
          FontWeight.w500,
          1.3,
        );
        expectStyle(
          NordicTypography.h2,
          'Be Vietnam Pro',
          22,
          FontWeight.w500,
          1.4,
        );
      },
    );

    test('display tightens tracking by -0.02 em with tight leading', () {
      const display = NordicTypography.display;
      expect(display.fontSize, 30.0);
      expect(display.letterSpacing, closeTo(-0.02 * 30.0, 1e-9));
      expect(display.height, 1.1);
      expect(display.fontWeight, FontWeight.w700);
    });

    test('body, label and metric use Be Vietnam Pro', () {
      const jakarta = 'Be Vietnam Pro';
      expectStyle(NordicTypography.h3, jakarta, 18, FontWeight.w400, 1.6);
      expectStyle(
        NordicTypography.bodyLarge,
        jakarta,
        16,
        FontWeight.w400,
        1.5,
      );
      expectStyle(
        NordicTypography.bodyRegular,
        jakarta,
        14,
        FontWeight.w400,
        1.5,
      );
      expectStyle(
        NordicTypography.labelMedium,
        jakarta,
        14,
        FontWeight.w500,
        1.4,
      );
      expectStyle(
        NordicTypography.labelSmall,
        jakarta,
        12,
        FontWeight.w500,
        1.4,
      );
      expectStyle(
        NordicTypography.priceHighlight,
        jakarta,
        24,
        FontWeight.w600,
        1.2,
      );
    });
  });
}
