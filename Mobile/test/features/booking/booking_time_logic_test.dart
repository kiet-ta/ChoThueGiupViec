// Decisions D34 (BOOK-TIME-01): the customer picks the start time and the length of a Cơ bản job inside the shift.
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/booking/logic/booking_logic.dart';
import 'package:mobile/features/booking/models/booking_models.dart';

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
}
