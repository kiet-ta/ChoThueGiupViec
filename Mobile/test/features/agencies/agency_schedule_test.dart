import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_response.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/agencies/logic/agency_schedule_logic.dart';
import 'package:mobile/features/agencies/screens/agency_schedule_screen.dart';
import 'package:mobile/features/workers/models/booking_slot_models.dart';
import 'package:mobile/features/workers/services/workers_slots_service.dart';

BookingSlotDto _slot(
  int id,
  String date,
  String shift,
  String start,
  String end, {
  bool active = true,
}) => BookingSlotDto(
  slotId: id,
  workerId: 7,
  slotDate: date,
  shiftCode: shift,
  startTime: start,
  endTime: end,
  isActive: active,
);

class _FakeSlots extends WorkersSlotsService {
  List<BookingSlotDto> slots = [];
  bool fail = false;
  bool offline = false;
  String? lastStart;
  String? lastEnd;

  @override
  Future<ApiResponse<List<BookingSlotDto>>> getSlots({
    required String startDate,
    required String endDate,
  }) async {
    lastStart = startDate;
    lastEnd = endDate;
    if (offline) throw Exception('offline');
    return fail
        ? const ApiResponse(success: false, message: 'Lỗi máy chủ.')
        : ApiResponse(success: true, message: '', data: slots);
  }
}

Future<void> _pump(WidgetTester tester, _FakeSlots service) async {
  tester.view.physicalSize = const Size(390, 1400);
  tester.view.devicePixelRatio = 1.0;
  addTearDown(() {
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
  await tester.pumpWidget(
    MaterialApp(
      theme: NordicTheme.lightTheme,
      home: AgencyScheduleScreen(
        slotsService: service,
        today: DateTime(2026, 10, 14),
      ),
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  group('agency schedule logic', () {
    test('names shifts, days and time ranges', () {
      expect(scheduleShiftName('SHIFT_AFTERNOON'), 'Ca chiều');
      expect(scheduleShiftName('OTHER'), 'OTHER');
      expect(dayTitle('2026-10-14'), 'T4 14/10');
      expect(dayTitle('bad'), 'bad');
      expect(timeRange('08:00:00', '12:00'), '08:00 – 12:00');
      expect(isoDate(DateTime(2026, 1, 5)), '2026-01-05');
    });

    test(
      'groups by day in date order, shifts by start, and drops inactive',
      () {
        final days = groupSchedule([
          _slot(3, '2026-10-15', 'SHIFT_EVENING', '17:30', '20:30'),
          _slot(2, '2026-10-14', 'SHIFT_AFTERNOON', '13:00', '17:00'),
          _slot(1, '2026-10-14', 'SHIFT_MORNING', '08:00', '12:00'),
          _slot(
            4,
            '2026-10-14',
            'SHIFT_EVENING',
            '17:30',
            '20:30',
            active: false,
          ),
        ]);

        expect(days.map((d) => d.slotDate), ['2026-10-14', '2026-10-15']);
        expect(days[0].slots.map((s) => s.slotId), [1, 2]);
        expect(days[1].slots.single.slotId, 3);
      },
    );
  });

  group('AgencyScheduleScreen', () {
    testWidgets('shows the shifts of the next 14 days grouped by day', (
      tester,
    ) async {
      final service = _FakeSlots()
        ..slots = [
          _slot(1, '2026-10-14', 'SHIFT_MORNING', '08:00', '12:00'),
          _slot(2, '2026-10-15', 'SHIFT_AFTERNOON', '13:00', '17:00'),
        ];
      await _pump(tester, service);

      expect(service.lastStart, '2026-10-14');
      expect(service.lastEnd, '2026-10-27');
      expect(find.text('T4 14/10'), findsOneWidget);
      expect(find.text('T5 15/10'), findsOneWidget);
      expect(find.text('Ca sáng'), findsOneWidget);
      expect(find.text('13:00 – 17:00'), findsOneWidget);
      expect(find.byKey(const Key('slot-1')), findsOneWidget);
    });

    testWidgets(
      'counts the shifts of the week and of the 14 days, and fills the grid',
      (tester) async {
        final service = _FakeSlots()
          ..slots = [
            _slot(1, '2026-10-14', 'SHIFT_MORNING', '08:00', '12:00'),
            _slot(2, '2026-10-15', 'SHIFT_AFTERNOON', '13:00', '17:00'),
            _slot(3, '2026-10-20', 'SHIFT_MORNING', '08:00', '12:00'),
          ];
        await _pump(tester, service);

        Text value(String key) => tester.widget<Text>(
          find
              .descendant(of: find.byKey(Key(key)), matching: find.byType(Text))
              .last,
        );
        // The StatTile value is a rich text; read the plain text of the tile instead.
        expect(
          find.descendant(
            of: find.byKey(const Key('schedule-week-count')),
            matching: find.textContaining('2', findRichText: true),
          ),
          findsOneWidget,
        );
        expect(
          find.descendant(
            of: find.byKey(const Key('schedule-total-count')),
            matching: find.textContaining('3', findRichText: true),
          ),
          findsOneWidget,
        );

        Color? cell(String date, String code) =>
            (tester
                        .widget<Container>(
                          find.byKey(Key('roster-cell-$date-$code')),
                        )
                        .decoration
                    as BoxDecoration)
                .color;
        expect(
          cell('2026-10-14', 'SHIFT_MORNING'),
          isNot(cell('2026-10-14', 'SHIFT_EVENING')),
        );
        expect(value, isNotNull);
      },
    );

    testWidgets('says so when the agency has not rostered the worker', (
      tester,
    ) async {
      await _pump(tester, _FakeSlots());

      expect(find.byKey(const Key('schedule-empty')), findsOneWidget);
    });

    testWidgets('a failed load shows the reason and retries', (tester) async {
      final service = _FakeSlots()..fail = true;
      await _pump(tester, service);
      expect(find.text('Lỗi máy chủ.'), findsOneWidget);

      service.fail = false;
      service.slots = [
        _slot(1, '2026-10-14', 'SHIFT_MORNING', '08:00', '12:00'),
      ];
      await tester.tap(find.byKey(const Key('schedule-retry')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('slot-1')), findsOneWidget);
    });

    testWidgets('a network failure is a plain message', (tester) async {
      await _pump(tester, _FakeSlots()..offline = true);

      expect(find.text('Không kết nối được máy chủ.'), findsOneWidget);
    });
  });
}
