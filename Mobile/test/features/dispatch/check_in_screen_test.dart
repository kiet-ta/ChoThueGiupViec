import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/device/camera_wrapper.dart';
import 'package:mobile/core/device/location_wrapper.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/network/api_response.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/dispatch/models/check_in_result.dart';
import 'package:mobile/features/dispatch/screens/check_in_screen.dart';
import 'package:mobile/features/dispatch/services/dispatch_service.dart';
import 'package:mobile/features/workers/models/worker_assignment_models.dart';
import 'package:mobile/features/workers/services/workers_assignments_service.dart';

/// Answers `GET /api/v1/workers/assignments/{id}` from a queue of answers; counts the calls.
class _FakeAssignments extends WorkersAssignmentsService {
  _FakeAssignments(this.answers) : super(apiClient: ApiClient());

  final List<ApiResponse<WorkerAssignmentDetailDto>> answers;
  int calls = 0;

  @override
  Future<ApiResponse<WorkerAssignmentDetailDto>> getAssignment(
    int assignmentId,
  ) async {
    final index = calls < answers.length ? calls : answers.length - 1;
    calls++;
    return answers[index];
  }
}

const _detail = WorkerAssignmentDetailDto(
  assignmentId: 1001,
  orderId: 6,
  orderCode: 'GV-6',
  slotDate: '2026-11-05',
  shiftCode: 'SHIFT_MORNING',
  startTime: '08:00',
  endTime: '12:00',
  status: 'ASSIGNED',
  serviceTier: 'ECONOMY',
  addressLine: '12 Nguyễn Huệ',
  district: 'Quận 1',
  city: 'TP. Hồ Chí Minh',
  latitude: 10.774,
  longitude: 106.703,
  payoutAmount: 128000,
);

const _address = '12 Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh';

ApiResponse<WorkerAssignmentDetailDto> _ok() =>
    const ApiResponse(success: true, message: '', data: _detail);

void main() {
  group('CheckInScreen Widget Tests', () {
    late DispatchService service;
    late LocationWrapper locationWrapper;
    late CameraWrapper cameraWrapper;

    setUp(() {
      locationWrapper = LocationWrapper(
        mockLocation: GeoPoint(
          latitude: 10.774,
          longitude: 106.703,
          timestamp: DateTime.now(),
        ),
      );
      cameraWrapper = CameraWrapper(
        mockImage: CapturedImage(
          path: '/mock/photos/plate_1001.jpg',
          capturedAt: DateTime.now(),
        ),
      );
      service = DispatchService(
        locationWrapper: locationWrapper,
        cameraWrapper: cameraWrapper,
        isMock: true,
      );
    });

    Widget createScreen({_FakeAssignments? assignments}) {
      return MaterialApp(
        theme: NordicTheme.lightTheme,
        home: CheckInScreen(
          assignmentId: 1001,
          dispatchService: service,
          assignmentsService: assignments ?? _FakeAssignments([_ok()]),
        ),
      );
    }

    testWidgets(
      'renders check in screen with destination address and GPS button',
      (tester) async {
        tester.view.physicalSize = const Size(390, 844);
        tester.view.devicePixelRatio = 1.0;
        addTearDown(() {
          tester.view.resetPhysicalSize();
          tester.view.resetDevicePixelRatio();
        });

        await tester.pumpWidget(createScreen());
        await tester.pumpAndSettle();

        expect(find.text('Đến nơi'), findsOneWidget);
        expect(find.text('Đã đến địa chỉ ca làm việc?'), findsOneWidget);
        expect(find.text(_address), findsOneWidget);
        expect(find.textContaining('Cầu Giấy'), findsNothing);
        expect(find.byKey(const Key('check_in_gps_button')), findsOneWidget);
      },
    );

    testWidgets(
      'a failed read of the job shows the error, no sample address and no check-in button',
      (tester) async {
        tester.view.physicalSize = const Size(390, 844);
        tester.view.devicePixelRatio = 1.0;
        addTearDown(() {
          tester.view.resetPhysicalSize();
          tester.view.resetDevicePixelRatio();
        });

        final assignments = _FakeAssignments([
          const ApiResponse(
            success: false,
            message: 'Job assignment 1001 not found.',
          ),
        ]);
        await tester.pumpWidget(createScreen(assignments: assignments));
        await tester.pumpAndSettle();

        expect(find.text('Job assignment 1001 not found.'), findsOneWidget);
        expect(find.textContaining('Cầu Giấy'), findsNothing);
        expect(find.byKey(const Key('check_in_gps_button')), findsNothing);
        expect(assignments.calls, 1);
      },
    );

    testWidgets('Thử lại reads the job again and then shows its address', (
      tester,
    ) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      final assignments = _FakeAssignments([
        const ApiResponse(success: false, message: 'Không kết nối được.'),
        _ok(),
      ]);
      await tester.pumpWidget(createScreen(assignments: assignments));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('check_in_gps_button')), findsNothing);

      await tester.tap(find.text('Thử lại'));
      await tester.pumpAndSettle();

      expect(assignments.calls, 2);
      expect(find.text(_address), findsOneWidget);
      expect(find.byKey(const Key('check_in_gps_button')), findsOneWidget);
    });

    testWidgets(
      'successful GPS check-in (distance <= 100m) shows success view',
      (tester) async {
        tester.view.physicalSize = const Size(390, 844);
        tester.view.devicePixelRatio = 1.0;
        addTearDown(() {
          tester.view.resetPhysicalSize();
          tester.view.resetDevicePixelRatio();
        });

        await tester.pumpWidget(createScreen());
        await tester.pumpAndSettle();

        // Tap GPS check in
        await tester.tap(find.byKey(const Key('check_in_gps_button')));
        await tester.pumpAndSettle();

        expect(find.text('Bạn đã đến nơi'), findsOneWidget);
        expect(find.text('Đã xác nhận bằng vị trí của bạn.'), findsOneWidget);
        expect(find.byKey(const Key('continue_to_job_button')), findsOneWidget);
      },
    );

    testWidgets(
      'GPS check-in deviating > 100m presents alternative verification methods',
      (tester) async {
        tester.view.physicalSize = const Size(390, 844);
        tester.view.devicePixelRatio = 1.0;
        addTearDown(() {
          tester.view.resetPhysicalSize();
          tester.view.resetDevicePixelRatio();
        });

        // Inject mock result with distance > 100m
        service.setMockCheckInResult(
          CheckInResult(
            checkInId: 2002,
            assignmentId: 1001,
            checkedInAt: DateTime.now(),
            distanceMeters: 250.0,
            isGpsVerified: false,
            requiresAlternativeVerification: true,
            verificationMethod: 'GPS',
            isCustomerConfirmed: false,
          ),
        );

        await tester.pumpWidget(createScreen());
        await tester.pumpAndSettle();

        await tester.tap(find.byKey(const Key('check_in_gps_button')));
        await tester.pumpAndSettle();

        expect(find.text('Vị trí chưa khớp'), findsOneWidget);
        expect(find.textContaining('250 mét'), findsOneWidget);
        expect(
          find.byKey(const Key('take_plate_photo_button')),
          findsOneWidget,
        );
        expect(
          find.byKey(const Key('request_customer_confirmation_button')),
          findsOneWidget,
        );

        // Verify taking plate photo fallback
        await tester.tap(find.byKey(const Key('take_plate_photo_button')));
        await tester.pumpAndSettle();

        expect(find.text('Bạn đã đến nơi'), findsOneWidget);
        expect(find.text('Đã xác nhận bằng ảnh biển số nhà.'), findsOneWidget);
      },
    );
  });
}
