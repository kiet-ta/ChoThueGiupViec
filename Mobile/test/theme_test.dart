import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/theme/nordic_colors.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/core/theme/nordic_typography.dart';

/// Every NordicColors constant that has a DESIGN.md YAML token, as
/// `YAML token name -> (constant, DESIGN.md hex)`.
const Map<String, (Color, String)> _tokenColors = {
  'canvas-background': (NordicColors.canvas, 'FAF8F3'),
  'card-surface': (NordicColors.surface, 'FFFFFF'),
  'surface-container': (NordicColors.surfaceContainer, 'F0EEE9'),
  'surface-dim': (NordicColors.surfaceDim, 'DBDAD5'),
  'primary': (NordicColors.primary, '416448'),
  'primary-hover': (NordicColors.primaryHover, '56705B'),
  'primary-container': (NordicColors.primaryContainer, '597D60'),
  'text-primary': (NordicColors.textTitle, '2F3A34'),
  'text-secondary': (NordicColors.textSecondary, '6B7280'),
  'on-primary': (NordicColors.textInverse, 'FFFFFF'),
  'outline-variant': (NordicColors.borderVariant, 'C2C8BF'),
  'error': (NordicColors.error, 'BA1A1A'),
  'error-container': (NordicColors.errorContainer, 'FFDAD6'),
};

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

    test('the accent amber is the DESIGN.md Brand Accent', () {
      expect(NordicColors.accentAmber, const Color(0xFFD9A441));
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
