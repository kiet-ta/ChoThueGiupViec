// Decisions D34 (BOOK-TIME-01): the customer picks the start time and the length of a Cơ bản job inside the shift.
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/booking/logic/booking_logic.dart';
import 'package:mobile/features/booking/models/booking_models.dart';
import 'package:mobile/features/booking/services/booking_service.dart';
import 'package:mobile/features/customers/models/worker_free_shift.dart';

const _options = BookingOptions(
  serviceTiers: ['ECONOMY', 'PREMIUM'],
  shifts: [],
  premiumMinLeadHours: 4,
  shiftMaxHours: 4,
  standardMaxAreaM2: 80,
  paymentQrExpiryMinutes: 15,
  sandbox: true,
);

const _morning = ShiftOption(
  shiftCode: 'SHIFT_MORNING',
  startLocal: '08:00',
  endLocal: '12:00',
);
const _evening = ShiftOption(
  shiftCode: 'SHIFT_EVENING',
  startLocal: '17:30',
  endLocal: '20:30',
);

void main() {
  test('lengths go from 2 h to the shift maximum in half hours', () {
    expect(durationChoices(_options), [2, 2.5, 3, 3.5, 4]);
  });

  test('hours and job time read as Vietnamese', () {
    expect(hoursLabel(2), '2 giờ');
    expect(hoursLabel(2.5), '2,5 giờ');
    expect(endTimeOf('08:30', 2.5), '11:00');
    expect(jobTimeText('08:30', 3), '08:30 – 11:30 (3 giờ)');
  });

  test('start times fit the job inside the shift', () {
    final day = DateTime.utc(2026, 11, 20);
    final now = DateTime.utc(2026, 11, 15, 3); // another day

    expect(
      startTimeChoices(
        shift: _morning,
        hours: 3,
        day: day,
        now: now,
        options: _options,
      ),
      ['08:00', '08:30', '09:00'],
    );
    expect(
      startTimeChoices(
        shift: _morning,
        hours: 4,
        day: day,
        now: now,
        options: _options,
      ),
      ['08:00'],
    );
    // The evening shift is 3 h long: a 4 h job has no start time.
    expect(
      startTimeChoices(
        shift: _evening,
        hours: 4,
        day: day,
        now: now,
        options: _options,
      ),
      isEmpty,
    );
  });

  test('today, a job starts at least the lead time from now', () {
    final day = DateTime.utc(2026, 11, 15);
    // 08:10 in Ho Chi Minh City: the earliest start is 09:10, rounded up to 09:30.
    final now = DateTime.utc(2026, 11, 15, 1, 10);

    expect(
      startTimeChoices(
        shift: _morning,
        hours: 2,
        day: day,
        now: now,
        options: _options,
      ),
      ['09:30', '10:00'],
    );
  });

  test('a missing start time is asked for only when the tier needs one', () {
    final day = DateTime.utc(2026, 11, 20);
    expect(
      missingChoice(
        tier: 'ECONOMY',
        addressId: 1,
        day: day,
        shiftCode: 'SHIFT_MORNING',
        hasQuote: true,
        needsTime: true,
      ),
      'Chọn giờ bắt đầu.',
    );
    expect(
      missingChoice(
        tier: 'PREMIUM',
        addressId: 1,
        day: day,
        shiftCode: 'SHIFT_MORNING',
        hasQuote: true,
      ),
      isNull,
    );
  });

  test('the request carries the time only when one was picked', () {
    const timed = CreateOrderInput(
      addressId: 1,
      serviceTier: 'ECONOMY',
      scheduledDate: '2026-11-20',
      shiftCode: 'SHIFT_MORNING',
      startTime: '08:30',
      durationHours: 3,
    );
    expect(timed.toJson()['startTime'], '08:30');
    expect(timed.toJson()['durationHours'], 3);

    const whole = CreateOrderInput(
      addressId: 1,
      serviceTier: 'PREMIUM',
      scheduledDate: '2026-11-20',
      shiftCode: 'SHIFT_MORNING',
    );
    expect(whole.toJson().containsKey('startTime'), isFalse);
  });

  test('the quote and the options read the D34 fields', () {
    final quote = PriceQuote.fromJson({
      'totalAmount': 195000,
      'durationHours': 3,
      'suggestedDurationHours': 2.5,
    });
    expect(quote.durationHours, 3);
    expect(quote.suggestedDurationHours, 2.5);

    final options = BookingOptions.fromJson({'economyMinLeadMinutes': 90});
    expect(options.economyMinLeadMinutes, 90);
    expect(options.startStepMinutes, 30);
  });

  // ---- BOOK-BUSY-01 (decisions D41): the chosen worker's own jobs ----

  group('a chosen worker who is busy', () {
    const afternoon = ['13:00', '13:30', '14:00', '14:30', '15:00'];
    ShiftBusy busy(List<(String, String)> times, [double? left]) =>
        ShiftBusy.fromTimes([
          for (final t in times) (start: t.$1, end: t.$2),
        ], left);

    test(
      'keeps every start when nothing is known or the worker has no job',
      () {
        expect(startsWorkerCanTake(afternoon, 2, null), afternoon);
        expect(startsWorkerCanTake(afternoon, 2, busy([])), afternoon);
        expect(startsWorkerCanTake(afternoon, 2, busy([], 8)), afternoon);
      },
    );

    test('drops the starts that overlap a live job (already widened by the buffer)', () {
      // A job until 13:00 and the 30 minutes after it: nothing may start before 13:30.
      expect(startsWorkerCanTake(afternoon, 2, busy([('11:30', '13:30')], 6)), [
        '13:30',
        '14:00',
        '14:30',
        '15:00',
      ]);
    });

    test('drops a start whose job would run into the next live job', () {
      // A 2 h job from 13:30 ends at 15:30, where the busy time begins; from 14:00 it overlaps.
      expect(startsWorkerCanTake(afternoon, 2, busy([('15:30', '17:00')])), [
        '13:00',
        '13:30',
      ]);
    });

    test('offers nothing when the shift is taken or the day has too few hours left', () {
      expect(
        startsWorkerCanTake(afternoon, 2, busy([('07:30', '17:30')])),
        isEmpty,
      );
      expect(startsWorkerCanTake(afternoon, 2, busy([], 1.5)), isEmpty);
      expect(startsWorkerCanTake(afternoon, 2, busy([], 2)), afternoon);
    });

    test('ignores a pair that is not a time', () {
      expect(startsWorkerCanTake(afternoon, 2, busy([('x', 'y')])), afternoon);
    });

    test('reads the server shift: windows, the hours left, and the older shape without them', () {
      final shifts = workerFreeShiftsOf([
        WorkerFreeShift.fromJson({
          'date': '2026-10-15',
          'shiftCode': 'SHIFT_MORNING',
          'busyWindows': [
            {'startTime': '07:30', 'endTime': '11:00'},
          ],
          'remainingHours': 5.5,
        }),
        WorkerFreeShift.fromJson({
          'date': '2026-10-16',
          'shiftCode': 'SHIFT_EVENING',
        }),
      ]);

      expect(shifts.keys, {
        '2026-10-15|SHIFT_MORNING',
        '2026-10-16|SHIFT_EVENING',
      });
      final morning = shifts.busy['2026-10-15|SHIFT_MORNING']!;
      expect(morning.windows, [(start: 450, end: 660)]);
      expect(morning.remainingHours, 5.5);
      final evening = shifts.busy['2026-10-16|SHIFT_EVENING']!;
      expect(evening.windows, isEmpty);
      expect(evening.remainingHours, isNull);
    });
  });
}
