import 'dart:math' as math;

import 'package:flutter/painting.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/theme/nordic_colors.dart';

/// WCAG relative luminance and contrast ratio.
double _luminance(Color c) {
  double channel(double v) =>
      v <= 0.03928 ? v / 12.92 : math.pow((v + 0.055) / 1.055, 2.4).toDouble();
  return 0.2126 * channel(c.r) + 0.7152 * channel(c.g) + 0.0722 * channel(c.b);
}

double _contrast(Color a, Color b) {
  final la = _luminance(a);
  final lb = _luminance(b);
  return (math.max(la, lb) + 0.05) / (math.min(la, lb) + 0.05);
}

void main() {
  group('the dark Cao cấp tile keeps its text readable (MOB-BOOK-02)', () {
    const surfaces = [NordicColors.contrastDark, NordicColors.contrastCard];

    test('white text (title, bullets) is at least 4.5:1 on both ends of the gradient', () {
      for (final surface in surfaces) {
        expect(
          _contrast(NordicColors.textInverse, surface),
          greaterThanOrEqualTo(4.5),
          reason: 'white on $surface',
        );
      }
    });

    test(
      'the quiet "?" and radio colour is at least 3:1 (an icon) on both ends',
      () {
        for (final surface in surfaces) {
          expect(
            _contrast(NordicColors.onDarkMuted, surface),
            greaterThanOrEqualTo(3.0),
            reason: 'onDarkMuted on $surface',
          );
        }
      },
    );

    test(
      'the amber accent is at least 3:1 on both ends (icon, check, border)',
      () {
        for (final surface in surfaces) {
          expect(
            _contrast(NordicColors.accentAmber, surface),
            greaterThanOrEqualTo(3.0),
            reason: 'amber on $surface',
          );
        }
      },
    );

    test('the dark green text on the amber tag is at least 4.5:1', () {
      expect(
        _contrast(NordicColors.primary, NordicColors.accentAmber),
        greaterThanOrEqualTo(4.5),
      );
    });
  });
}
