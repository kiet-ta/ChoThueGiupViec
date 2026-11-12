import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/network/api_response.dart';
import 'package:mobile/features/workers/models/booking_slot_models.dart';
import 'package:mobile/features/dispatch/routes.dart';
import 'package:mobile/features/workers/models/worker_assignment_models.dart';
import 'package:mobile/features/workers/models/worker_location_models.dart';
import 'package:mobile/features/workers/routes.dart';
import 'package:mobile/features/workers/screens/worker_slots_screen.dart';
import 'package:mobile/features/workers/services/map_launcher.dart';
import 'package:mobile/features/workers/services/workers_assignments_service.dart';
import 'package:mobile/features/workers/services/workers_location_service.dart';
import 'package:mobile/features/workers/services/workers_slots_service.dart';

import 'design_frame.dart';

class MockWorkersSlotsService extends WorkersSlotsService {
  ToggleBookingSlotRequest? lastToggleRequest;
  int toggleCalls = 0;

  /// When set, `toggleSlot` throws it (a refused or failed call).
  Object? toggleError;
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
    toggleCalls++;
    lastToggleRequest = request;
    if (toggleError != null) throw toggleError!;
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

class FakeMapLauncher implements IMapLauncher {
  final bool result;
  final List<Uri> opened = [];
  FakeMapLauncher({this.result = true});

  @override
  Future<bool> open(Uri uri) async {
    opened.add(uri);
    return result;
  }
}

class FakeAssignmentsService extends WorkersAssignmentsService {
  List<WorkerAssignmentDto> jobs = [];
  bool failing = false;
  bool detailFailing = false;
  double latitude = 10.8031;
  double longitude = 106.7147;
  int calls = 0;

  @override
  Future<ApiResponse<WorkerAssignmentDetailDto>> getAssignment(
    int assignmentId,
  ) async {
    if (detailFailing) throw Exception('offline');
    return ApiResponse<WorkerAssignmentDetailDto>(
      success: true,
      message: 'OK',
      data: WorkerAssignmentDetailDto(
        assignmentId: assignmentId,
        orderId: assignmentId,
        orderCode: 'GV-$assignmentId',
        slotDate: '2026-10-12',
        shiftCode: 'SHIFT_MORNING',
        startTime: '08:00',
        endTime: '12:00',
        status: 'ASSIGNED',
        serviceTier: 'ECONOMY',
        addressLine: '208 Nguyễn Hữu Cảnh',
        district: 'Bình Thạnh',
        city: 'TP. Hồ Chí Minh',
        latitude: latitude,
        longitude: longitude,
        payoutAmount: 221000,
      ),
    );
  }

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
      late FakeMapLauncher launcher;

      setUp(() {
        launcher = FakeMapLauncher();
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
              mapLauncher: launcher,
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

      Future<void> tapDirections(WidgetTester tester) async {
        await pumpScreen(tester);
        await tapVisible(tester, find.byKey(const Key('order-10002')));
        await tester.tap(find.byKey(const Key('order-sheet-route')));
        await tester.pumpAndSettle();
      }

      testWidgets('Chỉ đường opens the phone map app at the house', (
        tester,
      ) async {
        await tapDirections(tester);

        expect(launcher.opened, hasLength(1));
        final uri = launcher.opened.single;
        expect(uri.host, 'www.google.com');
        expect(uri.queryParameters['destination'], '10.8031,106.7147');
        expect(uri.queryParameters['travelmode'], 'two-wheeler');
      });

      testWidgets('Chỉ đường without coordinates uses the written address', (
        tester,
      ) async {
        assignments
          ..latitude = 0
          ..longitude = 0;
        await tapDirections(tester);

        expect(
          launcher.opened.single.queryParameters['destination'],
          '208 Nguyễn Hữu Cảnh, Bình Thạnh, TP. Hồ Chí Minh',
        );
      });

      testWidgets('Chỉ đường says so when the job cannot be read', (
        tester,
      ) async {
        assignments.detailFailing = true;
        await tapDirections(tester);

        expect(launcher.opened, isEmpty);
        expect(
          find.text('Chưa tải được địa chỉ của ca. Vui lòng thử lại.'),
          findsOneWidget,
        );
      });

      testWidgets('Chỉ đường says so when no map app is installed', (
        tester,
      ) async {
        launcher = FakeMapLauncher(result: false);
        await tapDirections(tester);

        expect(
          find.textContaining('Không mở được ứng dụng bản đồ'),
          findsOneWidget,
        );
      });

      testWidgets('an order under way has no route button', (tester) async {
        await pumpScreen(tester);

        await tapVisible(tester, find.byKey(const Key('order-10003')));

        expect(find.byKey(const Key('order-sheet-route')), findsNothing);
      });

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

    group('MOB-SLOTS-ERR-01 past days and plain-language errors', () {
      Future<void> open(WidgetTester tester, DateTime initial) async {
        useDesignFrame(tester);
        await tester.pumpWidget(
          MaterialApp(
            home: WorkerSlotsScreen(
              slotsService: mockService,
              locationService: mockLocation,
              initialDate: initial,
            ),
          ),
        );
        await tester.pumpAndSettle();
      }

      testWidgets('a past day has disabled switches, a hint and no request', (
        tester,
      ) async {
        await open(
          tester,
          DateTime(2026, 10, 14),
        ); // Wednesday; Monday 12/10 is past
        await tapVisible(tester, find.byKey(const Key('slots-day-0')));
        await tester.pumpAndSettle();

        expect(find.byKey(const Key('slots-past-day-hint')), findsOneWidget);
        final switches = tester.widgetList<Switch>(find.byType(Switch));
        expect(switches, isNotEmpty);
        expect(switches.every((sw) => sw.onChanged == null), isTrue);
        expect(mockService.toggleCalls, 0);
      });

      testWidgets('today still toggles and shows no past-day hint', (
        tester,
      ) async {
        await open(tester, DateTime(2026, 10, 14));

        expect(find.byKey(const Key('slots-past-day-hint')), findsNothing);
        await tapVisible(tester, find.byType(Switch).first);
        await tester.pumpAndSettle();
        expect(mockService.toggleCalls, 1);
      });

      testWidgets(
        'a slotDate 400 shows the past-day sentence, not the server text',
        (tester) async {
          mockService.toggleError = const ApiException(
            400,
            'Validation failed',
            fieldErrors: {
              'slotDate': ['Cannot toggle availability slots in the past.'],
            },
          );
          await open(tester, fixedDate);
          await tapVisible(tester, find.byType(Switch).first);
          await tester.pumpAndSettle();

          expect(find.textContaining('Validation failed'), findsNothing);
          expect(find.textContaining('Cannot toggle'), findsNothing);
          expect(
            find.text(
              'Ngày này đã qua nên không thể thay đổi lịch rảnh. Hãy chọn từ hôm nay trở đi.',
            ),
            findsOneWidget,
          );
        },
      );

      testWidgets(
        'at 19:15 every shift of today is locked with a clear sentence',
        (tester) async {
          await open(
            tester,
            DateTime(2026, 10, 14, 19, 15),
          ); // Wednesday evening

          for (final shift in [
            'SHIFT_MORNING',
            'SHIFT_AFTERNOON',
            'SHIFT_EVENING',
          ]) {
            expect(
              find.byKey(Key('shift-started-2026-10-14-$shift')),
              findsOneWidget,
            );
          }
          final switches = tester.widgetList<Switch>(find.byType(Switch));
          expect(switches.every((sw) => sw.onChanged == null), isTrue);
          expect(mockService.toggleCalls, 0);
        },
      );

      testWidgets(
        'at 10:00 only the morning shift is locked; the others still toggle',
        (tester) async {
          await open(tester, DateTime(2026, 10, 14, 10, 0));

          expect(
            find.byKey(const Key('shift-started-2026-10-14-SHIFT_MORNING')),
            findsOneWidget,
          );
          expect(
            find.byKey(const Key('shift-started-2026-10-14-SHIFT_AFTERNOON')),
            findsNothing,
          );
          final switches = tester
              .widgetList<Switch>(find.byType(Switch))
              .toList();
          expect(switches[0].onChanged, isNull);
          expect(switches[1].onChanged, isNotNull);
          expect(switches[2].onChanged, isNotNull);
        },
      );

      testWidgets('a later day is not locked even late in the evening', (
        tester,
      ) async {
        await open(tester, DateTime(2026, 10, 14, 19, 15));
        await tapVisible(
          tester,
          find.byKey(const Key('slots-day-3')),
        ); // Thursday
        await tester.pumpAndSettle();

        expect(find.textContaining('đã bắt đầu'), findsNothing);
        expect(
          tester
              .widgetList<Switch>(find.byType(Switch))
              .every((sw) => sw.onChanged != null),
          isTrue,
        );
      });

      testWidgets('a SHIFT_ALREADY_STARTED answer shows its own sentence', (
        tester,
      ) async {
        mockService.toggleError = const ApiException(
          409,
          'x',
          code: 'SHIFT_ALREADY_STARTED',
        );
        await open(tester, fixedDate);
        await tapVisible(tester, find.byType(Switch).first);
        await tester.pumpAndSettle();

        expect(
          find.text(
            'Ca này đã bắt đầu hoặc đã qua giờ nên không thể bật hoặc tắt.',
          ),
          findsOneWidget,
        );
      });

      testWidgets(
        'SLOT_IN_USE, a network error and anything else have their own sentence',
        (tester) async {
          final cases = <Object, String>{
            const ApiException(409, 'x', code: 'SLOT_IN_USE'): 'Ca này đang có đơn (đang mời hoặc đã nhận) nên không thể bật hoặc tắt.',
            const ApiException(0, 'Lỗi kết nối: SocketException'):
                'Không có kết nối mạng. Vui lòng thử lại.',
            Exception('boom'):
                'Không thể thay đổi lịch rảnh lúc này. Vui lòng thử lại sau.',
          };
          for (final entry in cases.entries) {
            mockService.toggleError = entry.key;
            await open(tester, fixedDate);
            await tapVisible(tester, find.byType(Switch).first);
            await tester.pumpAndSettle();
            expect(find.text(entry.value), findsOneWidget);
            expect(find.textContaining('boom'), findsNothing);
            expect(find.textContaining('SocketException'), findsNothing);
          }
        },
      );
    });
  });
}
