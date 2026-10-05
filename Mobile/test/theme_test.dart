import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/theme/nordic_colors.dart';
import 'package:mobile/core/theme/nordic_theme.dart';

void main() {
  group('NordicColors & NordicTheme Tests', () {
    test('NordicColors hex values match design specification', () {
      expect(NordicColors.canvas, const Color(0xFFFAF8F5));
      expect(NordicColors.primary, const Color(0xFF2D5A43));
      expect(NordicColors.secondaryAccent, const Color(0xFF6B8F71));
      expect(NordicColors.accentAmber, const Color(0xFFD99B26));
      expect(NordicColors.contrastDark, const Color(0xFF1A2F25));
      expect(NordicColors.textTitle, const Color(0xFF1F2923));
      expect(NordicColors.textBody, const Color(0xFF4B5563));
      expect(NordicColors.border, const Color(0xFFE5E7EB));
    });

    test('NordicTheme configured with lightTheme properties', () {
      final theme = NordicTheme.lightTheme;
      expect(theme.useMaterial3, isTrue);
      expect(theme.scaffoldBackgroundColor, NordicColors.canvas);
      expect(theme.colorScheme.primary, NordicColors.primary);
      expect(theme.colorScheme.secondary, NordicColors.secondaryAccent);
    });
  });
}
