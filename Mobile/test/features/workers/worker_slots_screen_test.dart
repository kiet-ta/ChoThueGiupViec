import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_response.dart';
import 'package:mobile/features/workers/models/booking_slot_models.dart';
import 'package:mobile/features/dispatch/routes.dart';
import 'package:mobile/features/workers/models/worker_assignment_models.dart';
import 'package:mobile/features/workers/models/worker_location_models.dart';
import 'package:mobile/features/workers/routes.dart';
import 'package:mobile/features/workers/screens/worker_slots_screen.dart';
import 'package:mobile/features/workers/services/workers_assignments_service.dart';
import 'package:mobile/features/workers/services/workers_location_service.dart';
import 'package:mobile/features/workers/services/workers_slots_service.dart';

import 'design_frame.dart';

class MockWorkersSlotsService extends WorkersSlotsService {
  ToggleBookingSlotRequest? lastToggleRequest;
  List<BookingSlotDto> slotsToReturn = [];

  @override
  Future<ApiResponse<List<BookingSlotDto>>> getSlots({
    required String startDate,
    required String endDate,
  }) async {
    return ApiResponse<List<BookingSlotDto>>(
      success: true,
      message: 'OK',
      data: slotsToReturn,
    );
  }

  @override
  Future<ApiResponse<BookingSlotDto>> toggleSlot(
    ToggleBookingSlotRequest request,
  ) async {
    lastToggleRequest = request;
    return ApiResponse<BookingSlotDto>(
      success: true,
      message: 'OK',
      data: BookingSlotDto(
        slotId: 501,
        workerId: 101,
        slotDate: request.slotDate,
        shiftCode: request.shiftCode,
        startTime: '08:00',
        endTime: '12:00',
        isActive: request.isActive,
      ),
    );
  }
}

class FakeAssignmentsService extends WorkersAssignmentsService {
  List<WorkerAssignmentDto> jobs = [];
  bool failing = false;
  int calls = 0;

  @override
  Future<ApiResponse<List<WorkerAssignmentDto>>> getAssignments({
    required String startDate,
    required String endDate,
  }) async {
    calls++;
    if (failing) throw Exception('offline');
    return ApiResponse<List<WorkerAssignmentDto>>(
      success: true,
      message: 'OK',
      data: jobs
          .where(
            (j) =>
                j.slotDate.compareTo(startDate) >= 0 &&
                j.slotDate.compareTo(endDate) <= 0,
          )
          .toList(),
    );
  }
}

WorkerAssignmentDto job(
  int id,
  String date,
  String shift,
  String status, {
  String tier = 'ECONOMY',
}) => WorkerAssignmentDto(
  assignmentId: id,
  orderId: id,
  orderCode: 'GV-$id',
  slotDate: date,
  shiftCode: shift,
  startTime: shift == 'SHIFT_MORNING' ? '08:00' : '13:00',
  endTime: shift == 'SHIFT_MORNING' ? '12:00' : '17:00',
  status: status,
  serviceTier: tier,
  district: 'Quận 1',
  payoutAmount: 128000,
);

class FailingSlotsService extends MockWorkersSlotsService {
  int calls = 0;

  @override
  Future<ApiResponse<List<BookingSlotDto>>> getSlots({
    required String startDate,
    required String endDate,
  }) {
    calls++;
    return calls == 1
        ? Future.error(Exception('Mất kết nối'))
        : super.getSlots(startDate: startDate, endDate: endDate);
  }
}

class MockWorkersLocationService extends WorkersLocationService {
  int shareCount = 0;
  bool failing = false;

  @override
  Future<ApiResponse<WorkerLocationDto>> shareCurrentLocation() async {
    shareCount++;
    if (failing) {
      throw Exception('offline');
    }
    return const ApiResponse<WorkerLocationDto>(success: true, message: 'OK');
  }
}

void main() {
  group('WorkerSlotsScreen Tests', () {
    late MockWorkersSlotsService mockService;
    late MockWorkersLocationService mockLocation;
    final fixedDate = DateTime(2026, 10, 12); // Monday, Oct 12, 2026

    setUp(() {
      mockService = MockWorkersSlotsService();
      mockLocation = MockWorkersLocationService();
      mockService.slotsToReturn = [
        BookingSlotDto(
          slotId: 501,
          workerId: 101,
          slotDate: '2026-10-12',
          shiftCode: 'SHIFT_MORNING',
          startTime: '08:00',
          endTime: '12:00',
          isActive: true,
        ),
      ];
    });

    testWidgets('WorkerSlotsScreen renders weekly header and shift cards', (
      tester,
    ) async {
      useDesignFrame(tester);
      await tester.pumpWidget(
        MaterialApp(
          home: WorkerSlotsScreen(
            slotsService: mockService,
            locationService: mockLocation,
            initialDate: fixedDate,
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Lịch rảnh'), findsOneWidget);
      expect(find.text('12/10 – 18/10/2026'), findsOneWidget);

      // Verify shifts rendered
      expect(find.text('Ca Sáng'), findsWidgets);
      expect(find.text('Ca Chiều'), findsWidgets);
      expect(find.text('Ca Tối'), findsWidgets);
    });

    testWidgets('Toggling a shift switch invokes toggleSlot API', (
      tester,
    ) async {
      useDesignFrame(tester);
      await tester.pumpWidget(
        MaterialApp(
          home: WorkerSlotsScreen(
            slotsService: mockService,
            locationService: mockLocation,
            initialDate: fixedDate,
          ),
        ),
      );
      await tester.pumpAndSettle();

      // Find first switch
      final switchFinder = find.byType(Switch).first;
      await tapVisible(tester, switchFinder);
      await tester.pumpAndSettle();

      expect(mockService.lastToggleRequest, isNotNull);
      expect(mockService.lastToggleRequest!.slotDate, '2026-10-12');
      expect(mockService.lastToggleRequest!.shiftCode, 'SHIFT_MORNING');
    });

    testWidgets('Opening the screen shares the position once', (tester) async {
      useDesignFrame(tester);
      await tester.pumpWidget(
        MaterialApp(
          home: WorkerSlotsScreen(
            slotsService: mockService,
            locationService: mockLocation,
            initialDate: fixedDate,
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(mockLocation.shareCount, 1);
    });

    testWidgets('Switching a slot ON shares the position, OFF does not', (
      tester,
    ) async {
      useDesignFrame(tester);
      await tester.pumpWidget(
        MaterialApp(
          home: WorkerSlotsScreen(
            slotsService: mockService,
            locationService: mockLocation,
            initialDate: fixedDate,
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(mockLocation.shareCount, 1);

      // The first switch (Monday morning) is ON: switching it OFF sends nothing.
      await tapVisible(tester, find.byType(Switch).first);
      await tester.pumpAndSettle();
      expect(mockService.lastToggleRequest!.isActive, isFalse);
      expect(mockLocation.shareCount, 1);

      // The second switch (Monday afternoon) is OFF: switching it ON sends the position.
      await tapVisible(tester, find.byType(Switch).at(1));
      await tester.pumpAndSettle();
      expect(mockService.lastToggleRequest!.isActive, isTrue);
      expect(mockLocation.shareCount, 2);
    });

    testWidgets('A failing position call is ignored by the screen', (
      tester,
    ) async {
      mockLocation.failing = true;
      useDesignFrame(tester);
      await tester.pumpWidget(
        MaterialApp(
          home: WorkerSlotsScreen(
            slotsService: mockService,
            locationService: mockLocation,
            initialDate: fixedDate,
          ),
        ),
      );
      await tester.pumpAndSettle();

      await tapVisible(tester, find.byType(Switch).at(1));
      await tester.pumpAndSettle();

      expect(mockLocation.shareCount, 2);
      expect(find.textContaining('Không thể thay đổi lịch rảnh'), findsNothing);
      expect(tester.takeException(), isNull);
    });

    testWidgets('A week that cannot be loaded shows a retry that reloads it', (
      tester,
    ) async {
      final failing = FailingSlotsService();
      useDesignFrame(tester);
      await tester.pumpWidget(
        MaterialApp(
          home: WorkerSlotsScreen(
            slotsService: failing,
            locationService: mockLocation,
            assignmentsService: FakeAssignmentsService(),
            initialDate: fixedDate,
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.textContaining('Mất kết nối'), findsOneWidget);
      expect(find.byType(Switch), findsNothing);

      await tapVisible(tester, find.text('Thử lại'));

      expect(failing.calls, 2);
      expect(find.byType(Switch), findsNWidgets(3));
    });

    group('booked orders in the schedule', () {
      late FakeAssignmentsService assignments;

      setUp(() {
        assignments = FakeAssignmentsService()
          ..jobs = [
            job(10002, '2026-10-12', 'SHIFT_MORNING', 'ASSIGNED'),
            job(10003, '2026-10-12', 'SHIFT_AFTERNOON', 'CHECKED_IN'),
            job(
              10004,
              '2026-10-13',
              'SHIFT_MORNING',
              'COMPLETED',
              tier: 'PREMIUM',
            ),
          ];
      });

      Future<void> pumpScreen(WidgetTester tester) async {
        useDesignFrame(tester);
        await tester.pumpWidget(
          MaterialApp(
            routes: {
              DispatchRoutes.checkIn: (c) => Scaffold(
                body: Text('checkin:${ModalRoute.of(c)!.settings.arguments}'),
              ),
              WorkerRoutes.execution: (c) => Scaffold(
                body: Text('execution:${ModalRoute.of(c)!.settings.arguments}'),
              ),
            },
            home: WorkerSlotsScreen(
              slotsService: mockService,
              locationService: mockLocation,
              assignmentsService: assignments,
              initialDate: fixedDate,
            ),
          ),
        );
        await tester.pumpAndSettle();
      }

      testWidgets('marks the days that have an order on the strip', (
        tester,
      ) async {
        await pumpScreen(tester);

        expect(find.byKey(const Key('slots-day-order-0')), findsOneWidget);
        expect(find.byKey(const Key('slots-day-order-1')), findsOneWidget);
        expect(find.byKey(const Key('slots-day-order-2')), findsNothing);
        expect(find.text('Ngày có đơn'), findsOneWidget);
      });

      testWidgets(
        'a shift with an order shows the order, not a switch; the others keep theirs',
        (tester) async {
          await pumpScreen(tester); // Monday 12/10 is chosen

          expect(find.text('Đã nhận'), findsOneWidget);
          expect(find.text('Đang làm'), findsOneWidget);
          expect(find.text('GV-10002'), findsOneWidget);
          expect(find.textContaining('Quận 1 · Cơ bản'), findsNWidgets(2));
          expect(find.text('128.000 đ'), findsNWidgets(2));
          expect(
            find.byKey(const Key('shift-booked-2026-10-12-SHIFT_MORNING')),
            findsOneWidget,
          );
          // Sáng and Chiều are booked, only Tối keeps a switch.
          expect(find.byType(Switch), findsOneWidget);
        },
      );

      testWidgets(
        'an order not started opens a sheet that leads to the check-in',
        (tester) async {
          await pumpScreen(tester);

          await tapVisible(tester, find.byKey(const Key('order-10002')));

          final sheet = find.byKey(const Key('order-sheet-10002'));
          expect(sheet, findsOneWidget);
          expect(
            find.descendant(of: sheet, matching: find.text('Chi tiết ca')),
            findsOneWidget,
          );
          expect(
            find.descendant(of: sheet, matching: find.text('Thứ 2, 12/10')),
            findsOneWidget,
          );
          expect(
            find.descendant(of: sheet, matching: find.text('GV-10002')),
            findsOneWidget,
          );

          await tester.tap(find.byKey(const Key('order-sheet-action')));
          await tester.pumpAndSettle();

          expect(find.text('checkin:10002'), findsOneWidget);
        },
      );

      testWidgets('an order under way leads to the work screen', (
        tester,
      ) async {
        await pumpScreen(tester);

        await tapVisible(tester, find.byKey(const Key('order-10003')));
        expect(find.text('Tiếp tục ca'), findsOneWidget);
        await tester.tap(find.byKey(const Key('order-sheet-action')));
        await tester.pumpAndSettle();

        expect(find.text('execution:10003'), findsOneWidget);
      });

      testWidgets('a finished order has no button', (tester) async {
        await pumpScreen(tester);
        await tapVisible(tester, find.byKey(const Key('slots-day-1'))); // 13/10

        expect(find.text('Hoàn tất'), findsOneWidget);
        expect(find.textContaining('Quận 1 · Cao cấp'), findsOneWidget);
        await tapVisible(tester, find.byKey(const Key('order-10004')));

        expect(find.byKey(const Key('order-sheet-10004')), findsOneWidget);
        expect(find.byKey(const Key('order-sheet-action')), findsNothing);
      });

      testWidgets(
        'a failed read of the orders keeps the switches and can be retried',
        (tester) async {
          assignments.failing = true;
          await pumpScreen(tester);

          expect(find.byKey(const Key('orders-error')), findsOneWidget);
          expect(find.byType(Switch), findsNWidgets(3));
          expect(find.byKey(const Key('order-10002')), findsNothing);

          assignments.failing = false;
          await tester.tap(find.byKey(const Key('orders-retry')));
          await tester.pumpAndSettle();

          expect(find.byKey(const Key('orders-error')), findsNothing);
          expect(find.byKey(const Key('order-10002')), findsOneWidget);
        },
      );

      testWidgets('another week asks for the orders of that week', (
        tester,
      ) async {
        await pumpScreen(tester);
        final before = assignments.calls;

        await tapVisible(tester, find.byIcon(Icons.chevron_right));

        expect(assignments.calls, before + 1);
        expect(find.byKey(const Key('slots-day-order-0')), findsNothing);
      });
    });

    testWidgets('Navigating week updates week date range', (tester) async {
      useDesignFrame(tester);
      await tester.pumpWidget(
        MaterialApp(
          home: WorkerSlotsScreen(
            slotsService: mockService,
            locationService: mockLocation,
            initialDate: fixedDate,
          ),
        ),
      );
      await tester.pumpAndSettle();

      // Tap next week
      await tapVisible(tester, find.byIcon(Icons.chevron_right));
      await tester.pumpAndSettle();

      expect(find.text('19/10 – 25/10/2026'), findsOneWidget);
    });
  });
}
