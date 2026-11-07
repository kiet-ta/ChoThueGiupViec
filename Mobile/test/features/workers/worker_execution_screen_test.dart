import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/widgets/nordic_button.dart';
import 'package:mobile/features/workers/screens/worker_execution_screen.dart';

import 'design_frame.dart';
import 'fake_assignments_service.dart';

Widget screen(FakeAssignmentsService assignments) => MaterialApp(
  home: WorkerExecutionScreen(
    assignmentId: 2001,
    assignmentsService: assignments,
  ),
);

NordicButton submitButton(WidgetTester tester) => tester.widget<NordicButton>(
  find.byKey(const Key('submit_completion_button')),
);

void main() {
  group('WorkerExecutionScreen Tests', () {
    testWidgets('WorkerExecutionScreen renders header, work zone and timer', (
      tester,
    ) async {
      useDesignFrame(tester);
      await tester.pumpWidget(
        MaterialApp(
          home: WorkerExecutionScreen(
            assignmentId: 2001,
            assignmentsService: FakeAssignmentsService(),
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
        MaterialApp(
          home: WorkerExecutionScreen(
            assignmentId: 2001,
            assignmentsService: FakeAssignmentsService(),
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

    testWidgets(
      'A job already awaiting acceptance cannot be submitted again (#677)',
      (tester) async {
        useDesignFrame(tester);
        await tester.pumpWidget(
          screen(FakeAssignmentsService(status: 'AWAITING_ACCEPTANCE')),
        );
        await tester.pumpAndSettle();

        expect(find.text('Gửi nghiệm thu'), findsNothing);
        expect(
          find.text('Đã gửi nghiệm thu, chờ gia chủ xác nhận'),
          findsOneWidget,
        );
        expect(submitButton(tester).onPressed, isNull);
      },
    );

    testWidgets('An IN_PROGRESS job can still be submitted', (tester) async {
      useDesignFrame(tester);
      await tester.pumpWidget(screen(FakeAssignmentsService()));
      await tester.pumpAndSettle();

      expect(find.text('Gửi nghiệm thu'), findsOneWidget);
      expect(submitButton(tester).onPressed, isNotNull);
    });

    testWidgets('When the status cannot be read the submit button stays on', (
      tester,
    ) async {
      useDesignFrame(tester);
      await tester.pumpWidget(
        screen(
          FakeAssignmentsService(failure: const ApiException(0, 'offline')),
        ),
      );
      await tester.pumpAndSettle();

      expect(submitButton(tester).onPressed, isNotNull);
    });
  });
}
