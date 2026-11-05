import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/device/camera_wrapper.dart';
import 'package:mobile/core/device/device_location.dart';
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

/// A phone that cannot give its position.
class _NoPosition implements IDeviceLocationProvider {
  @override
  Future<GeoPoint> currentPosition() async =>
      throw const DeviceLocationException(
        DeviceLocationFailure.serviceDisabled,
      );
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

/// The same job with another status (#659).
ApiResponse<WorkerAssignmentDetailDto> _status(String status) => ApiResponse(
  success: true,
  message: '',
  data: WorkerAssignmentDetailDto(
    assignmentId: _detail.assignmentId,
    orderId: _detail.orderId,
    orderCode: _detail.orderCode,
    slotDate: _detail.slotDate,
    shiftCode: _detail.shiftCode,
    startTime: _detail.startTime,
    endTime: _detail.endTime,
    status: status,
    serviceTier: _detail.serviceTier,
    addressLine: _detail.addressLine,
    district: _detail.district,
    city: _detail.city,
    latitude: _detail.latitude,
    longitude: _detail.longitude,
    payoutAmount: _detail.payoutAmount,
  ),
);

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
      'a phone that cannot give its position shows why and never checks the worker in',
      (tester) async {
        tester.view.physicalSize = const Size(390, 844);
        tester.view.devicePixelRatio = 1.0;
        addTearDown(() {
          tester.view.resetPhysicalSize();
          tester.view.resetDevicePixelRatio();
        });

        service = DispatchService(
          locationWrapper: LocationWrapper(provider: _NoPosition()),
          cameraWrapper: cameraWrapper,
          isMock: true,
        );
        await tester.pumpWidget(createScreen());
        await tester.pumpAndSettle();

        await tester.tap(find.byKey(const Key('check_in_gps_button')));
        await tester.pumpAndSettle();

        expect(
          find.text(
            deviceLocationFailureText(DeviceLocationFailure.serviceDisabled),
          ),
          findsOneWidget,
        );
        expect(find.text('Bạn đã đến nơi'), findsNothing);
      },
    );

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

    Future<void> phone(WidgetTester tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });
    }

    Future<void> farCheckIn(WidgetTester tester) async {
      service.setMockCheckInResult(
        CheckInResult(
          checkInId: 2002,
          assignmentId: 1001,
          checkedInAt: DateTime.now(),
          distanceMeters: 6675.0,
          isGpsVerified: false,
          requiresAlternativeVerification: true,
          verificationMethod: 'GPS',
          isCustomerConfirmed: false,
        ),
      );
      await tester.tap(find.byKey(const Key('check_in_gps_button')));
      await tester.pumpAndSettle();
    }

    testWidgets(
      'asking the customer waits for the confirmation and never claims it (#659)',
      (tester) async {
        await phone(tester);
        final assignments = _FakeAssignments([_ok(), _status('ASSIGNED')]);
        await tester.pumpWidget(createScreen(assignments: assignments));
        await tester.pumpAndSettle();
        await farCheckIn(tester);

        await tester.tap(
          find.byKey(const Key('request_customer_confirmation_button')),
        );
        await tester.pump();

        expect(find.text('Đang chờ gia chủ xác nhận'), findsOneWidget);
        expect(find.text('Chờ xác nhận'), findsOneWidget);
        expect(find.text('Bạn đã đến nơi'), findsNothing);
        expect(find.text('Gia chủ đã xác nhận bạn đến nơi.'), findsNothing);
        expect(find.byKey(const Key('continue_to_job_button')), findsNothing);
        expect(
          find.byKey(const Key('check_customer_confirmation_button')),
          findsOneWidget,
        );

        // Still ASSIGNED on the next read: still waiting.
        await tester.pump(const Duration(seconds: 5));
        expect(find.text('Đang chờ gia chủ xác nhận'), findsOneWidget);
        expect(assignments.calls, greaterThanOrEqualTo(3));

        // Leaving the screen stops the poll.
        await tester.pumpWidget(const SizedBox());
        final calls = assignments.calls;
        await tester.pump(const Duration(seconds: 15));
        expect(assignments.calls, calls);
      },
    );

    testWidgets(
      'the customer confirmation turns the screen to the success view (#659)',
      (tester) async {
        await phone(tester);
        final assignments = _FakeAssignments([
          _ok(),
          _status('ASSIGNED'),
          _status('CHECKED_IN'),
        ]);
        await tester.pumpWidget(createScreen(assignments: assignments));
        await tester.pumpAndSettle();
        await farCheckIn(tester);
        await tester.tap(
          find.byKey(const Key('request_customer_confirmation_button')),
        );
        await tester.pump();
        expect(find.text('Đang chờ gia chủ xác nhận'), findsOneWidget);

        await tester.tap(
          find.byKey(const Key('check_customer_confirmation_button')),
        );
        await tester.pumpAndSettle();

        expect(find.text('Bạn đã đến nơi'), findsOneWidget);
        expect(find.text('Gia chủ đã xác nhận bạn đến nơi.'), findsOneWidget);
        expect(find.byKey(const Key('work_starts_hint')), findsOneWidget);
        expect(find.byKey(const Key('continue_to_job_button')), findsOneWidget);
      },
    );

    testWidgets(
      'opening an assignment that is already checked in shows it as done (#659)',
      (tester) async {
        await phone(tester);
        await tester.pumpWidget(
          createScreen(assignments: _FakeAssignments([_status('CHECKED_IN')])),
        );
        await tester.pumpAndSettle();

        expect(find.text('Bạn đã đến nơi'), findsOneWidget);
        expect(find.text('Bạn đã check-in cho ca làm này.'), findsOneWidget);
        expect(find.byKey(const Key('continue_to_job_button')), findsOneWidget);
        expect(find.byKey(const Key('check_in_gps_button')), findsNothing);
      },
    );
  });
}
