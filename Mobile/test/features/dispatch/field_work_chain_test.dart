import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_response.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/dispatch/routes.dart';
import 'package:mobile/features/dispatch/screens/dispatch_offers_screen.dart';
import 'package:mobile/features/dispatch/services/dispatch_service.dart';
import 'package:mobile/features/workers/routes.dart';
import 'package:mobile/features/workers/screens/worker_execution_screen.dart';
import 'package:mobile/features/workers/services/workers_photos_service.dart';

import '../workers/design_frame.dart';

/// #377: accept -> check-in -> execution -> photos / submit, each step carrying the real assignment id.
void main() {
  Widget recordingApp(Widget home, List<RouteSettings> pushed) => MaterialApp(
    theme: NordicTheme.lightTheme,
    home: home,
    onGenerateRoute: (settings) {
      pushed.add(settings);
      return MaterialPageRoute(
        builder: (_) => const Scaffold(body: Text('next')),
      );
    },
  );

  testWidgets(
    'after accepting, "Đến nơi & Check-in" opens check-in with the accepted id',
    (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(tester.view.reset);
      final pushed = <RouteSettings>[];
      await tester.pumpWidget(
        recordingApp(
          DispatchOffersScreen(dispatchService: DispatchService(isMock: true)),
          pushed,
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('accept_offer_button')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('go_to_check_in_button')));
      await tester.pumpAndSettle();

      expect(pushed.single.name, DispatchRoutes.checkIn);
      expect(pushed.single.arguments, 1001);
    },
  );

  testWidgets(
    'execution: photos opens with the id, submit calls submit-completion for it',
    (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(tester.view.reset);
      final pushed = <RouteSettings>[];
      final service = _RecordingPhotosService();
      await tester.pumpWidget(
        recordingApp(
          WorkerExecutionScreen(assignmentId: 7, photosService: service),
          pushed,
        ),
      );
      await tester.pump();

      final photos = find.text('Chụp Ảnh Before / After (VoL)');
      await tapVisible(tester, photos);
      await tester.pumpAndSettle();
      expect(pushed.single.name, WorkerRoutes.photos);
      expect(pushed.single.arguments, 7);

      Navigator.of(tester.element(find.text('next'))).pop();
      await tester.pumpAndSettle();
      await tapVisible(
        tester,
        find.byKey(const Key('submit_completion_button')),
      );
      await tester.pump();
      expect(service.submitted, [7]);
      expect(
        find.text('Đã gửi nghiệm thu. Chờ khách hàng xác nhận.'),
        findsOneWidget,
      );
    },
  );

  testWidgets(
    'a field-work route without an assignment id shows a note instead of a made-up job',
    (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          home: Builder(
            builder: WorkersFeatureModule().routes[WorkerRoutes.execution]!,
          ),
        ),
      );
      await tester.pump();
      expect(find.text('Hãy mở màn này từ một ca đã nhận.'), findsOneWidget);
    },
  );
}

class _RecordingPhotosService extends WorkersPhotosService {
  final submitted = <int>[];

  @override
  Future<ApiResponse<void>> submitCompletion(
    int assignmentId, {
    String? notes,
  }) async {
    submitted.add(assignmentId);
    return ApiResponse<void>(success: true, message: 'OK');
  }
}
