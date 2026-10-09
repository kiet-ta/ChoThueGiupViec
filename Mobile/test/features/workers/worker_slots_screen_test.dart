import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_response.dart';
import 'package:mobile/features/workers/models/booking_slot_models.dart';
import 'package:mobile/features/workers/screens/worker_slots_screen.dart';
import 'package:mobile/features/workers/services/workers_slots_service.dart';

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
  Future<ApiResponse<BookingSlotDto>> toggleSlot(ToggleBookingSlotRequest request) async {
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

void main() {
  group('WorkerSlotsScreen Tests', () {
    late MockWorkersSlotsService mockService;
    final fixedDate = DateTime(2026, 10, 12); // Monday, Oct 12, 2026

    setUp(() {
      mockService = MockWorkersSlotsService();
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

    testWidgets('WorkerSlotsScreen renders weekly header and shift cards', (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          home: WorkerSlotsScreen(
            slotsService: mockService,
            initialDate: fixedDate,
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Lịch Rảnh Ca Làm'), findsOneWidget);
      expect(find.text('Block Slots Lịch Tuần'), findsOneWidget);
      expect(find.text('12/10 – 18/10/2026'), findsOneWidget);

      // Verify shifts rendered
      expect(find.text('Ca Sáng'), findsWidgets);
      expect(find.text('Ca Chiều'), findsWidgets);
      expect(find.text('Ca Tối'), findsWidgets);
    });

    testWidgets('Toggling a shift switch invokes toggleSlot API', (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          home: WorkerSlotsScreen(
            slotsService: mockService,
            initialDate: fixedDate,
          ),
        ),
      );
      await tester.pumpAndSettle();

      // Find first switch
      final switchFinder = find.byType(Switch).first;
      await tester.tap(switchFinder);
      await tester.pumpAndSettle();

      expect(mockService.lastToggleRequest, isNotNull);
      expect(mockService.lastToggleRequest!.slotDate, '2026-10-12');
      expect(mockService.lastToggleRequest!.shiftCode, 'SHIFT_MORNING');
    });

    testWidgets('Navigating week updates week date range', (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          home: WorkerSlotsScreen(
            slotsService: mockService,
            initialDate: fixedDate,
          ),
        ),
      );
      await tester.pumpAndSettle();

      // Tap next week
      await tester.tap(find.byIcon(Icons.chevron_right));
      await tester.pumpAndSettle();

      expect(find.text('19/10 – 25/10/2026'), findsOneWidget);
    });
  });
}
