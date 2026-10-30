import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/workers/screens/worker_execution_screen.dart';

import 'design_frame.dart';

void main() {
  group('WorkerExecutionScreen Tests', () {
    testWidgets('WorkerExecutionScreen renders header, work zone and timer', (
      tester,
    ) async {
      useDesignFrame(tester);
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

      expect(find.text('Ca đang làm'), findsOneWidget);
      expect(find.text('Phòng khách & Bếp'), findsOneWidget);
      expect(find.text('Thời gian làm'), findsOneWidget);
      expect(find.text('01:00:00'), findsOneWidget);

      // The made-up checklist is gone: the API has none (issue #442).
      expect(find.text('Checklist Công Việc'), findsNothing);
    });

    testWidgets('Toggling timer button updates icon', (tester) async {
      useDesignFrame(tester);
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
      await tapVisible(tester, find.byIcon(Icons.pause_circle_filled));
      await tester.pumpAndSettle();

      expect(find.byIcon(Icons.play_circle_filled), findsOneWidget);
    });
  });
}
