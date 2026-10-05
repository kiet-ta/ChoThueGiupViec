import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/core/widgets/eyebrow_badge.dart';
import 'package:mobile/core/widgets/nordic_button.dart';
import 'package:mobile/core/widgets/nordic_card.dart';
import 'package:mobile/core/widgets/trust_badge.dart';

void main() {
  group('Nordic Core Widgets Tests', () {
    testWidgets('NordicButton renders and triggers onPressed', (tester) async {
      var pressed = false;
      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: Scaffold(
            body: NordicButton(
              label: 'Đặt Ca',
              onPressed: () => pressed = true,
            ),
          ),
        ),
      );

      expect(find.text('Đặt Ca'), findsOneWidget);
      await tester.tap(find.byType(NordicButton));
      await tester.pump();
      expect(pressed, isTrue);
    });

    testWidgets('NordicButton renders loading state', (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: Scaffold(
            body: NordicButton(
              label: 'Đang tải',
              isLoading: true,
              onPressed: () {},
            ),
          ),
        ),
      );

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
      expect(find.text('Đang tải'), findsNothing);
    });

    testWidgets('NordicCard renders child and responds to tap', (tester) async {
      var tapped = false;
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: NordicCard(
              isHighlighted: true,
              onTap: () => tapped = true,
              child: const Text('Thẻ Dịch Vụ'),
            ),
          ),
        ),
      );

      expect(find.text('Thẻ Dịch Vụ'), findsOneWidget);
      await tester.tap(find.text('Thẻ Dịch Vụ'));
      await tester.pump();
      expect(tapped, isTrue);
    });

    testWidgets('TrustBadge and EyebrowBadge render properly', (tester) async {
      await tester.pumpWidget(
        const MaterialApp(
          home: Scaffold(
            body: Column(
              children: [
                TrustBadge(text: 'Bảo hiểm 50 triệu'),
                EyebrowBadge(text: 'Tiêu Chuẩn Bắc Âu'),
              ],
            ),
          ),
        ),
      );

      expect(find.text('Bảo hiểm 50 triệu'), findsOneWidget);
      expect(find.text('TIÊU CHUẨN BẮC ÂU'), findsOneWidget);
    });
  });
}
