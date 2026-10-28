import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/theme/nordic_colors.dart';
import 'package:mobile/core/theme/nordic_radius.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/core/theme/nordic_typography.dart';

/// Every NordicColors constant that has a DESIGN.md YAML token, as
/// `YAML token name -> (constant, DESIGN.md hex)`.
/// `primary`, `primary-hover` and `primary-container` are left out on purpose: the Mobile app
/// uses its own palette for them (MOB-UI-09, owner decision 2026-10-28, see [_mobilePalette]).
const Map<String, (Color, String)> _tokenColors = {
  'canvas-background': (NordicColors.canvas, 'FAF8F3'),
  'card-surface': (NordicColors.surface, 'FFFFFF'),
  'surface-container': (NordicColors.surfaceContainer, 'F0EEE9'),
  'surface-dim': (NordicColors.surfaceDim, 'DBDAD5'),
  'text-primary': (NordicColors.textTitle, '2F3A34'),
  'text-secondary': (NordicColors.textSecondary, '6B7280'),
  'on-primary': (NordicColors.textInverse, 'FFFFFF'),
  'outline-variant': (NordicColors.borderVariant, 'C2C8BF'),
  'error': (NordicColors.error, 'BA1A1A'),
  'error-container': (NordicColors.errorContainer, 'FFDAD6'),
};

/// The Mobile palette (#2F6B3F / #7FB77E / #F7C85C / #FFF6C0) and the values derived from it.
const Map<String, (Color, int)> _mobilePalette = {
  'primary': (NordicColors.primary, 0xFF2F6B3F),
  'primaryHover': (NordicColors.primaryHover, 0xFF285C36),
  'primaryContainer': (NordicColors.primaryContainer, 0xFF7FB77E),
  'secondaryAccent': (NordicColors.secondaryAccent, 0xFF467F49),
  'primaryFixed': (NordicColors.primaryFixed, 0xFFD2E6D2),
  'accentAmber': (NordicColors.accentAmber, 0xFFF7C85C),
  'amberSoft': (NordicColors.amberSoft, 0xFFFFF6C0),
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
    });

    test(
      'only buttons are rounded, by 6 px; cards, sheets and inputs are square',
      () {
        expect(NordicRadius.button, 6.0);
        expect(NordicRadius.none, BorderRadius.zero);
        const buttonShape = RoundedRectangleBorder(
          borderRadius: BorderRadius.all(Radius.circular(6.0)),
        );
        final theme = NordicTheme.lightTheme;
        for (final style in [
          theme.elevatedButtonTheme.style,
          theme.filledButtonTheme.style,
          theme.outlinedButtonTheme.style,
          theme.textButtonTheme.style,
          theme.segmentedButtonTheme.style,
        ]) {
          expect(style!.shape!.resolve({}), buttonShape);
        }
        expect(theme.chipTheme.shape, buttonShape);

        const square = RoundedRectangleBorder(borderRadius: BorderRadius.zero);
        expect(
          (theme.cardTheme.shape! as RoundedRectangleBorder).borderRadius,
          BorderRadius.zero,
        );
        expect(theme.bottomSheetTheme.shape, square);
        expect(theme.dialogTheme.shape, square);
        expect(
          (theme.inputDecorationTheme.border! as OutlineInputBorder)
              .borderRadius,
          BorderRadius.zero,
        );
        expect(
          (theme.inputDecorationTheme.focusedBorder! as OutlineInputBorder)
              .borderRadius,
          BorderRadius.zero,
        );
      },
    );

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

    test('headlines use Newsreader', () {
      expectStyle(
        NordicTypography.headlineLg,
        'Newsreader',
        40,
        FontWeight.w600,
        1.2,
      );
      expectStyle(
        NordicTypography.headlineLgMobile,
        'Newsreader',
        32,
        FontWeight.w600,
        1.25,
      );
      expectStyle(NordicTypography.h1, 'Newsreader', 28, FontWeight.w500, 1.3);
      expectStyle(NordicTypography.h2, 'Newsreader', 22, FontWeight.w500, 1.4);
    });

    test('body, label and metric use Plus Jakarta Sans', () {
      const jakarta = 'Plus Jakarta Sans';
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
