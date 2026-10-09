import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/workers/screens/worker_execution_screen.dart';

void main() {
  group('WorkerExecutionScreen Tests', () {
    testWidgets('WorkerExecutionScreen renders header, timer, and checklist items', (tester) async {
      await tester.pumpWidget(
        const MaterialApp(
          home: WorkerExecutionScreen(
            assignmentId: 2001,
            workZone: 'Phòng khách & Bếp',
            initialElapsedSeconds: 3600, // 01:00:00
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Màn Thi Công #2001'), findsOneWidget);
      expect(find.text('Phòng khách & Bếp'), findsOneWidget);
      expect(find.text('Thời Gian Thi Công'), findsOneWidget);
      expect(find.text('01:00:00'), findsOneWidget);

      // Verify checklist items
      expect(find.text('Checklist Công Việc'), findsOneWidget);
      expect(find.text('2/5'), findsOneWidget);
      expect(find.text('Dọn dẹp & sắp xếp phòng khách'), findsOneWidget);
      expect(find.text('Lau chùi cửa kính & bề mặt'), findsOneWidget);
    });

    testWidgets('Tapping checklist item updates completion state and counter', (tester) async {
      await tester.pumpWidget(
        const MaterialApp(
          home: WorkerExecutionScreen(
            assignmentId: 2001,
            initialElapsedSeconds: 0,
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('2/5'), findsOneWidget);

      // Tap 3rd item (Hút bụi & lau sàn nhà)
      await tester.tap(find.text('Hút bụi & lau sàn nhà'));
      await tester.pumpAndSettle();

      // Counter increases to 3/5
      expect(find.text('3/5'), findsOneWidget);
    });

    testWidgets('Toggling timer button updates icon', (tester) async {
      await tester.pumpWidget(
        const MaterialApp(
          home: WorkerExecutionScreen(
            assignmentId: 2001,
            initialElapsedSeconds: 100,
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.byIcon(Icons.pause_circle_filled), findsOneWidget);

      // Tap pause button
      await tester.tap(find.byIcon(Icons.pause_circle_filled));
      await tester.pumpAndSettle();

      expect(find.byIcon(Icons.play_circle_filled), findsOneWidget);
    });
  });
}
