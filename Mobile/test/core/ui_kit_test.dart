import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/core/widgets/countdown_timer_widget.dart';
import 'package:mobile/core/widgets/mini_map_widget.dart';
import 'package:mobile/core/widgets/nordic_text_input.dart';
import 'package:mobile/core/widgets/status_state_widget.dart';

void main() {
  group('UI Kit Widgets Tests', () {
    testWidgets('CountdownTimerWidget displays seconds and decrements on tick', (
      WidgetTester tester,
    ) async {
      int tickCount = 0;
      bool timedOut = false;

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: Scaffold(
            body: CountdownTimerWidget(
              totalSeconds: 10,
              initialSeconds: 3,
              onTick: (sec) {
                tickCount = sec;
              },
              onTimeout: () {
                timedOut = true;
              },
            ),
          ),
        ),
      );

      expect(find.text('3s'), findsOneWidget);
      expect(find.text('Sắp hết thời gian nhận ca!'), findsOneWidget);

      await tester.pump(const Duration(seconds: 1));
      expect(find.text('2s'), findsOneWidget);
      expect(tickCount, 2);

      await tester.pump(const Duration(seconds: 1));
      expect(find.text('1s'), findsOneWidget);
      expect(tickCount, 1);

      await tester.pump(const Duration(seconds: 1));
      expect(find.text('0s'), findsOneWidget);
      expect(timedOut, isTrue);
    });

    testWidgets('MiniMapWidget renders address label, badge, and canvas', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: const Scaffold(
            body: MiniMapWidget(
              targetLatitude: 10.762622,
              targetLongitude: 106.660172,
              addressLabel: '123 Đường Nguyễn Huệ, Quận 1',
              distanceKm: 2.5,
            ),
          ),
        ),
      );

      expect(find.text('123 Đường Nguyễn Huệ, Quận 1'), findsOneWidget);
      expect(find.text('2.5 km'), findsOneWidget);
      expect(find.byType(CustomPaint), findsWidgets);
    });

    testWidgets('MiniMapWidget renders fallback radius badge when distanceKm is null', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: const Scaffold(
            body: MiniMapWidget(
              targetLatitude: 10.762622,
              targetLongitude: 106.660172,
              radiusMeters: 250.0,
            ),
          ),
        ),
      );

      expect(find.text('Bán kính 250m'), findsOneWidget);
    });

    testWidgets('NordicTextInput renders label, hint, and accepts user input', (
      WidgetTester tester,
    ) async {
      final controller = TextEditingController();
      String? changedValue;

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: Scaffold(
            body: NordicTextInput(
              label: 'Số điện thoại',
              hintText: 'Nhập số điện thoại',
              controller: controller,
              onChanged: (val) {
                changedValue = val;
              },
            ),
          ),
        ),
      );

      expect(find.text('Số điện thoại'), findsOneWidget);
      expect(find.text('Nhập số điện thoại'), findsOneWidget);

      await tester.enterText(find.byType(TextFormField), '0901234567');
      expect(changedValue, '0901234567');
      expect(controller.text, '0901234567');
    });

    testWidgets('LoadingStateWidget renders indicator and message', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: const Scaffold(
            body: LoadingStateWidget(message: 'Đang tải danh sách công việc...'),
          ),
        ),
      );

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
      expect(find.text('Đang tải danh sách công việc...'), findsOneWidget);
    });

    testWidgets('ErrorStateWidget renders error message and triggers retry callback', (
      WidgetTester tester,
    ) async {
      bool retryPressed = false;

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: Scaffold(
            body: ErrorStateWidget(
              title: 'Lỗi kết nối',
              message: 'Không thể kết nối tới máy chủ.',
              onRetry: () {
                retryPressed = true;
              },
            ),
          ),
        ),
      );

      expect(find.text('Lỗi kết nối'), findsOneWidget);
      expect(find.text('Không thể kết nối tới máy chủ.'), findsOneWidget);
      expect(find.text('Thử lại'), findsOneWidget);

      await tester.tap(find.text('Thử lại'));
      expect(retryPressed, isTrue);
    });

    testWidgets('EmptyStateWidget renders title, subtitle, and custom action', (
      WidgetTester tester,
    ) async {
      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: Scaffold(
            body: EmptyStateWidget(
              title: 'Không có đơn hàng',
              subtitle: 'Hiện tại chưa có công việc nào gần bạn.',
              action: ElevatedButton(
                onPressed: () {},
                child: const Text('Tìm kiếm việc mới'),
              ),
            ),
          ),
        ),
      );

      expect(find.text('Không có đơn hàng'), findsOneWidget);
      expect(find.text('Hiện tại chưa có công việc nào gần bạn.'), findsOneWidget);
      expect(find.text('Tìm kiếm việc mới'), findsOneWidget);
    });
  });
}
