import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/booking/logic/booking_logic.dart';
import 'package:mobile/features/booking/models/booking_models.dart';

const options = BookingOptions(
  serviceTiers: ['ECONOMY', 'PREMIUM'],
  shifts: [
    ShiftOption(shiftCode: 'SHIFT_MORNING', startLocal: '08:00', endLocal: '12:00'),
    ShiftOption(shiftCode: 'SHIFT_AFTERNOON', startLocal: '13:00', endLocal: '17:00'),
    ShiftOption(shiftCode: 'SHIFT_EVENING', startLocal: '17:30', endLocal: '20:30'),
  ],
  premiumMinLeadHours: 4,
  shiftMaxHours: 4,
  standardMaxAreaM2: 80,
  paymentQrExpiryMinutes: 15,
  sandbox: true,
);

PriceQuote quote({int workers = 1, double area = 50, int unit = 260000}) => PriceQuote(
      addressId: 3,
      serviceTier: 'ECONOMY',
      totalAreaM2: area,
      areaBracket: workers == 2 ? 'OVER_80' : 'FROM_31_TO_80',
      requiredWorkers: workers,
      unitPrice: unit,
      totalAmount: unit * workers,
      currency: 'VND',
    );

void main() {
  group('money and area', () {
    test('formats whole VND with dots', () {
      expect(formatVnd(0), '0 đ');
      expect(formatVnd(999), '999 đ');
      expect(formatVnd(1000), '1.000 đ');
      expect(formatVnd(260000), '260.000 đ');
      expect(formatVnd(1234567), '1.234.567 đ');
      expect(formatVnd(-5000), '-5.000 đ');
    });

    test('formats an area without a trailing zero and with a decimal comma', () {
      expect(formatArea(80), '80 m²');
      expect(formatArea(80.5), '80,5 m²');
      expect(formatArea(80.01), '80,01 m²');
    });
  });

  group('labels', () {
    test('names the two segments and explains them, with the lead time from the options', () {
      expect(tierLabel('ECONOMY'), 'Economy');
      expect(tierLabel('PREMIUM'), 'Premium');
      expect(tierLabel('GOLD'), 'GOLD');
      expect(tierDescription('ECONOMY', options), contains('Thợ tự do'));
      expect(tierDescription('PREMIUM', options), contains('ít nhất 4 giờ'));
    });

    test('names a shift with its local hours', () {
      expect(shiftText(options.shifts[0]), 'Ca sáng · 08:00–12:00');
      expect(shiftText(options.shifts[2]), 'Ca tối · 17:30–20:30');
      expect(shiftLabel('SOMETHING'), 'SOMETHING');
    });

    test('says one worker, or two workers above the standard area', () {
      expect(workersNote(quote(), options), '1 thợ, một ca tối đa 4 giờ.');
      expect(workersNote(quote(workers: 2, area: 90), options), contains('cần 2 thợ'));
      expect(workersNote(quote(workers: 2, area: 90), options), contains('lớn hơn 80 m²'));
    });
  });

  group('days in Asia/Ho_Chi_Minh', () {
    test('offers today and the next 14 days', () {
      final days = selectableDates(DateTime.utc(2026, 10, 14, 3));

      expect(days.length, selectableDays + 1);
      expect(dateKey(days.first), '2026-10-14');
      expect(dateKey(days.last), '2026-10-28');
    });

    test('at 17:00 UTC the local day is already the next one', () {
      expect(dateKey(selectableDates(DateTime.utc(2026, 10, 14, 17)).first), '2026-10-15');
      expect(dateKey(selectableDates(DateTime.utc(2026, 10, 14, 16, 59)).first), '2026-10-14');
    });

    test('crosses a month and a year', () {
      expect(dateKey(selectableDates(DateTime.utc(2026, 12, 25)).last), '2027-01-08');
    });

    test('labels a day with its weekday', () {
      expect(dayLabel(DateTime.utc(2026, 10, 15)), 'Th 5 15/10');
      expect(dayLabel(DateTime.utc(2026, 10, 18)), 'CN 18/10');
    });
  });

  group('validation', () {
    test('limits the note to 500 and the skill to 100 characters after trimming', () {
      expect(validateNote(''), isNull);
      expect(validateNote('a' * 500), isNull);
      expect(validateNote('  ${'a' * 500}  '), isNull);
      expect(validateNote('a' * 501), isNotNull);
      expect(validateSkill('a' * 100), isNull);
      expect(validateSkill('a' * 101), isNotNull);
    });

    test('tells what is still missing, in order', () {
      String? missing({String? tier, int? address, DateTime? day, String? shift, bool hasQuote = true}) =>
          missingChoice(tier: tier, addressId: address, day: day, shiftCode: shift, hasQuote: hasQuote);
      final day = DateTime.utc(2026, 10, 15);

      expect(missing(), contains('phân khúc'));
      expect(missing(tier: 'ECONOMY'), contains('địa chỉ'));
      expect(missing(tier: 'ECONOMY', address: 3), contains('ngày'));
      expect(missing(tier: 'ECONOMY', address: 3, day: day), contains('ca'));
      expect(missing(tier: 'ECONOMY', address: 3, day: day, shift: 'SHIFT_MORNING', hasQuote: false), contains('giá'));
      expect(missing(tier: 'ECONOMY', address: 3, day: day, shift: 'SHIFT_MORNING'), isNull);
    });
  });

  group('a failed order creation', () {
    test('explains each business code, and never depends on the message', () {
      String? text(String code) => createOrderFailure(ApiException(409, 'whatever the server wrote', code: code)).message;

      expect(text('SHIFT_IN_PAST'), contains('đã bắt đầu hoặc đã qua'));
      expect(text('PREMIUM_LEAD_TIME'), contains('ít nhất 4 giờ'));
      expect(text('FULLY_BOOKED'), contains('kín lịch'));
      expect(text('SOMETHING_NEW'), 'Không đặt được đơn lúc này. Vui lòng thử lại.');
      expect(createOrderFailure(const ApiException(409, 'No code')).message, 'Không đặt được đơn lúc này. Vui lòng thử lại.');
    });

    test('uses the lead time it is given', () {
      final failure = createOrderFailure(const ApiException(409, '', code: 'PREMIUM_LEAD_TIME'), premiumMinLeadHours: 6);

      expect(failure.message, contains('ít nhất 6 giờ'));
    });

    test('puts the 400 messages on their fields', () {
      final failure = createOrderFailure(const ApiException(400, 'Validation failed', fieldErrors: {
        'scheduledDate': ['scheduledDate must be at most 14 days ahead.'],
        'customerNote': ['too long', 'second'],
      }));

      expect(failure.message, isNull);
      expect(failure.fields, {'scheduledDate': 'scheduledDate must be at most 14 days ahead.', 'customerNote': 'too long'});
    });

    test('shows a message for a 400 without field errors, and for 401, 403, 404, 500 and no network', () {
      expect(createOrderFailure(const ApiException(400, 'Bad body')).message, 'Bad body');
      expect(createOrderFailure(const ApiException(401, '')).message, contains('đăng nhập lại'));
      expect(createOrderFailure(const ApiException(403, '')).message, contains('khách hàng'));
      expect(createOrderFailure(const ApiException(404, '')).message, contains('địa chỉ'));
      expect(createOrderFailure(const ApiException(500, 'stack trace')).message, 'Máy chủ gặp lỗi. Vui lòng thử lại sau.');
      expect(createOrderFailure(const ApiException(0, 'Không kết nối được máy chủ.')).message, 'Không kết nối được máy chủ.');
    });
  });

  test('a failed read is explained without leaking a server message', () {
    expect(loadFailureMessage(const ApiException(0, 'offline')), 'offline');
    expect(loadFailureMessage(const ApiException(401, 'x')), contains('đăng nhập lại'));
    expect(loadFailureMessage(const ApiException(403, 'x')), contains('khách hàng'));
    expect(loadFailureMessage(const ApiException(404, 'x')), 'Không tìm thấy dữ liệu.');
    expect(loadFailureMessage(const ApiException(500, 'NullReference at ...')), 'Máy chủ gặp lỗi. Vui lòng thử lại sau.');
  });
}
