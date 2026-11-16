// Pure logic of the booking screens (no Flutter, no network): labels, VND, days in Asia/Ho_Chi_Minh, validation, error texts.
import '../../../core/network/api_client.dart';
import '../models/booking_models.dart';

/// The contract's business codes of POST /api/v1/booking/orders (booking.md 3.3).
class BookingErrorCode {
  static const shiftInPast = 'SHIFT_IN_PAST';
  static const premiumLeadTime = 'PREMIUM_LEAD_TIME';
  static const fullyBooked = 'FULLY_BOOKED';

  /// Decisions D34: a timed Cơ bản job starts sooner than `economyMinLeadMinutes` from now.
  static const leadTimeTooShort = 'LEAD_TIME_TOO_SHORT';

  /// BOOK-BUSY-01 (decisions D41): the worker the customer chose is busy at the chosen time.
  static const preferredWorkerBusy = 'PREFERRED_WORKER_BUSY';

  BookingErrorCode._();
}

// ---- Decisions D34: the customer picks the start time and the length of a Cơ bản job inside the shift. ----

/// Minutes since midnight of "HH:mm"; null when the text is not a time.
int? minutesOf(String hhmm) {
  final parts = hhmm.split(':');
  if (parts.length != 2) return null;
  final h = int.tryParse(parts[0]);
  final m = int.tryParse(parts[1]);
  if (h == null || m == null) return null;
  return h * 60 + m;
}

/// "HH:mm" of minutes since midnight.
String hhmmOf(int minutes) => '${_two(minutes ~/ 60)}:${_two(minutes % 60)}';

/// The lengths offered: from `minDurationHours` to `shiftMaxHours` in `durationStepHours` (2, 2,5 … 4).
List<double> durationChoices(BookingOptions options) {
  final step = options.durationStepHours <= 0 ? 0.5 : options.durationStepHours;
  return [
    for (
      var h = options.minDurationHours;
      h <= options.shiftMaxHours + 1e-9;
      h += step
    )
      h,
  ];
}

/// `2` -> `2 giờ`, `2.5` -> `2,5 giờ`.
String hoursLabel(double hours) {
  final text = hours == hours.roundToDouble()
      ? hours.toStringAsFixed(0)
      : hours.toString().replaceAll('.', ',');
  return '$text giờ';
}

/// End "HH:mm" of a job that starts at [start] and lasts [hours].
String endTimeOf(String start, double hours) =>
    hhmmOf((minutesOf(start) ?? 0) + (hours * 60).round());

/// `08:30 – 11:30 (3 giờ)`.
String jobTimeText(String start, double hours) =>
    '$start – ${endTimeOf(start, hours)} (${hoursLabel(hours)})';

/// The start times a job of [hours] can take inside [shift] on [day]: on the start step, ending by the shift end, and (today only)
/// at least `economyMinLeadMinutes` after [now]. Empty when the shift is too short or already too late.
List<String> startTimeChoices({
  required ShiftOption shift,
  required double hours,
  required DateTime day,
  required DateTime now,
  required BookingOptions options,
}) {
  final shiftStart = minutesOf(shift.startLocal);
  final shiftEnd = minutesOf(shift.endLocal);
  if (shiftStart == null || shiftEnd == null) return const [];
  final length = (hours * 60).round();
  final step = options.startStepMinutes <= 0 ? 30 : options.startStepMinutes;

  var earliest = shiftStart;
  final localNow = now.toUtc().add(const Duration(hours: 7));
  if (hoChiMinhDay(now) == day) {
    earliest = [
      earliest,
      localNow.hour * 60 + localNow.minute + options.economyMinLeadMinutes,
    ].reduce((a, b) => a > b ? a : b);
  }
  // Round up to the step, counted from the shift start.
  final offset = earliest - shiftStart;
  final first = shiftStart + ((offset + step - 1) ~/ step) * step;
  return [for (var m = first; m + length <= shiftEnd; m += step) hhmmOf(m)];
}

/// BOOK-BUSY-01 (decisions D41): what a chosen worker's live jobs take on one day. A timed job keeps the worker's shift open (D34), so
/// [windows] (minutes since midnight, start inclusive, end exclusive, each already widened by the travel buffer) and [remainingHours]
/// are what tell a time the worker can take from one they cannot.
class ShiftBusy {
  final List<({int start, int end})> windows;

  /// Hours left of the worker's day; null = not known.
  final double? remainingHours;

  const ShiftBusy({this.windows = const [], this.remainingHours});

  /// From the server's "HH:mm" pairs; a pair that is not a time is ignored.
  factory ShiftBusy.fromTimes(
    Iterable<({String start, String end})> times,
    double? remainingHours,
  ) {
    final windows = <({int start, int end})>[];
    for (final t in times) {
      final start = minutesOf(t.start);
      final end = minutesOf(t.end);
      if (start != null && end != null) windows.add((start: start, end: end));
    }
    return ShiftBusy(windows: windows, remainingHours: remainingHours);
  }
}

/// The shifts a chosen worker is free for ([keys], see [freeShiftKey]) and, per shift, what their jobs already take ([busy]).
class WorkerFreeShifts {
  final Set<String> keys;
  final Map<String, ShiftBusy> busy;

  const WorkerFreeShifts(this.keys, [this.busy = const {}]);

  bool contains(String key) => keys.contains(key);
  bool get isEmpty => keys.isEmpty;
}

/// Of [starts] (start times for a job of [hours]) the ones the worker can take: the job fits in their remaining hours and overlaps none
/// of their busy times. Without [busy] every start stays.
List<String> startsWorkerCanTake(
  List<String> starts,
  double hours,
  ShiftBusy? busy,
) {
  if (busy == null) return starts;
  final left = busy.remainingHours;
  if (left != null && hours > left) return const [];
  if (busy.windows.isEmpty) return starts;
  final length = (hours * 60).round();
  return [
    for (final s in starts)
      if (minutesOf(s) case final from?)
        if (!busy.windows.any((w) => from < w.end && w.start < from + length))
          s,
  ];
}

const int customerNoteMaxLength = 500;

/// How many days ahead the screen offers. The server is the authority (`Booking.MaxDaysAhead`, default 14, decisions Q23 B3) and
/// answers 400 on `scheduledDate` beyond it; the options endpoint does not carry this number, so it is the one value kept here.
const int selectableDays = 14;

/// Whole VND with a dot as the thousands separator: `1234567` -> `1.234.567 đ`.
String formatVnd(int amount) {
  final digits = amount.abs().toString();
  final buffer = StringBuffer();
  for (var i = 0; i < digits.length; i++) {
    if (i > 0 && (digits.length - i) % 3 == 0) buffer.write('.');
    buffer.write(digits[i]);
  }
  return '${amount < 0 ? '-' : ''}$buffer đ';
}

/// `80.0` -> `80`, `80.5` -> `80,5` (Vietnamese decimal comma).
String formatArea(double areaM2) {
  final rounded = (areaM2 * 100).round() / 100;
  final text = rounded == rounded.roundToDouble()
      ? rounded.toStringAsFixed(0)
      : rounded.toString();
  return '${text.replaceAll('.', ',')} m²';
}

String tierLabel(String tier) => switch (tier) {
  ServiceTier.economy => 'Cơ bản',
  ServiceTier.premium => 'Cao cấp',
  _ => tier,
};

/// One line that tells the two segments apart (PRD 2.1): who does the work and how it is assigned.
String tierDescription(String tier, BookingOptions options) => switch (tier) {
  ServiceTier.economy =>
    'Phù hợp dọn dẹp thông thường. Người làm tự do nhận ca theo khu vực.',
  ServiceTier.premium =>
    'Người làm từ công ty đối tác. Đặt trước ít nhất ${options.premiumMinLeadHours} giờ.',
  _ => '',
};

/// The short tag of a package on its card (MOB-BOOK-01).
String tierTag(String tier) => switch (tier) {
  ServiceTier.economy => 'Tiết kiệm',
  ServiceTier.premium => 'An tâm tối đa',
  _ => '',
};

/// The two short lines on the card of a package (MOB-BOOK-01); the premium lead time is the options' (`premiumMinLeadHours`).
List<String> tierBullets(String tier, BookingOptions options) => switch (tier) {
  ServiceTier.economy => const [
    'Dọn dẹp tiêu chuẩn, thao tác nhanh, phù hợp nhu cầu hằng ngày.',
    'Hoàn tiền tự động 100% nếu không khớp được ca làm.',
  ],
  ServiceTier.premium => [
    'Nhân sự từ đơn vị đối tác uy tín (đặt trước ít nhất ${options.premiumMinLeadHours} giờ).',
    'Đảm bảo có người làm, hỗ trợ thay thế khẩn cấp khi phát sinh sự cố.',
  ],
  _ => const [],
};

/// The long text behind the "?" button of a package: its description, then what the card used to show under it.
List<String> tierDetails(String tier, BookingOptions options) => switch (tier) {
  ServiceTier.economy => [
    tierDescription(tier, options),
    'Đơn có thể tự động hoàn tiền nếu khu vực hết người nhận ca.',
  ],
  ServiceTier.premium => [
    tierDescription(tier, options),
    '• Đối tác cung cấp người làm theo lịch đã giữ.\n'
        '• Hỗ trợ điều phối thay thế khẩn cấp khi có sự cố; hoàn tiền nếu không tìm được người thay thế.\n'
        '• Bạn kiểm tra trực tiếp; người làm khắc phục tại chỗ trong 15–30 phút nếu cần.',
  ],
  _ => const [],
};

String shiftLabel(String shiftCode) => switch (shiftCode) {
  'SHIFT_MORNING' => 'Ca sáng',
  'SHIFT_AFTERNOON' => 'Ca chiều',
  'SHIFT_EVENING' => 'Ca tối',
  _ => shiftCode,
};

/// D36: what the customer reads about the 2nd job of a large house; null once the work ended in the first job (nothing to say).
String? continuationText(OrderContinuation? continuation) {
  if (continuation == null) return null;
  final start = continuation.windowStartAt;
  final end = continuation.windowEndAt;
  final hours = start != null && end != null
      ? end.difference(start).inHours
      : 0;
  final upTo = hours > 0 ? ' (tối đa $hours giờ)' : '';
  return switch (continuation.status) {
    OrderContinuation.standby =>
      'Nhà lớn: nếu thợ chưa làm xong, thợ sẽ làm thêm giờ ngay sau ca$upTo. Không tính thêm tiền.',
    OrderContinuation.open =>
      'Đơn nối tiếp đã mở: thợ làm thêm giờ ngay sau ca$upTo. Không tính thêm tiền.',
    _ => null,
  };
}

String shiftText(ShiftOption shift) =>
    '${shiftLabel(shift.shiftCode)} · ${shift.startLocal}–${shift.endLocal}';

/// The instant shown as a calendar day in Asia/Ho_Chi_Minh (UTC+7, no daylight saving).
DateTime hoChiMinhDay(DateTime instant) {
  final local = instant.toUtc().add(const Duration(hours: 7));
  return DateTime.utc(local.year, local.month, local.day);
}

/// The days the customer may pick: today (Ho Chi Minh) and the following [selectableDays] days.
List<DateTime> selectableDates(DateTime now) {
  final today = hoChiMinhDay(now);
  return [
    for (var i = 0; i <= selectableDays; i++) today.add(Duration(days: i)),
  ];
}

String _two(int n) => n.toString().padLeft(2, '0');

/// "yyyy-MM-dd", the `scheduledDate` of the contract.
/// A shift of a day as one key ("2026-10-15|SHIFT_MORNING"), to look up a worker's free shifts.
String freeShiftKey(String date, String shiftCode) => '$date|$shiftCode';

String dateKey(DateTime day) =>
    '${day.year}-${_two(day.month)}-${_two(day.day)}';

const _weekdays = ['Th 2', 'Th 3', 'Th 4', 'Th 5', 'Th 6', 'Th 7', 'CN'];

/// `Th 5 15/10`.
String dayLabel(DateTime day) =>
    '${_weekdays[day.weekday - 1]} ${_two(day.day)}/${_two(day.month)}';

/// The line under the price: one worker, a large ECONOMY house (one worker, a possible 2nd job, D36), or two workers (PREMIUM, BR-02).
String workersNote(PriceQuote quote, BookingOptions options) =>
    quote.isContinuousWork
    ? 'Nhà bạn lớn nên thợ có thể cần làm 2 đơn liên tiếp: đơn 2 là làm thêm giờ ngay sau ca (tối đa ${options.shiftMaxHours} giờ). Bạn chỉ trả một lần, không phát sinh thêm.'
    : quote.requiredWorkers >= 2
    ? 'Diện tích ${formatArea(quote.totalAreaM2)} lớn hơn ${formatArea(options.standardMaxAreaM2)}: cần ${quote.requiredWorkers} người làm cùng lúc.'
    : '1 người, một ca tối đa ${options.shiftMaxHours} giờ.';

/// The price line: unit price x workers, or the single large-house price (D36: one worker, so no "x N" to confuse the customer).
String quoteBreakdown(PriceQuote quote) => quote.isContinuousWork
    ? 'Giá nhà lớn · 1 người · ${tierLabel(quote.serviceTier)}'
    : '${formatVnd(quote.unitPrice)} × ${quote.requiredWorkers} người · ${tierLabel(quote.serviceTier)}';

/// The consent asked before a large house is booked (D36).
const List<String> continuousWorkConsent = [
  'Nhà bạn khá lớn nên chỉ một thợ nhận đơn. Nếu thợ chưa làm xong trong ca đã đặt, thợ sẽ làm tiếp "đơn 2" là thêm giờ ngay sau ca (tối đa 4 giờ).',
  'Bạn chỉ trả một lần theo giá nhà lớn. Đơn nối tiếp không bị tính thêm tiền.',
];

String? validateNote(String text) => text.trim().length > customerNoteMaxLength
    ? 'Ghi chú tối đa $customerNoteMaxLength ký tự (đang ${text.trim().length}).'
    : null;

/// What is still missing before the order can be created; null when everything is chosen.
String? missingChoice({
  required String? tier,
  required int? addressId,
  required DateTime? day,
  required String? shiftCode,
  required bool hasQuote,
  bool needsTime = false,
  String? startTime,
}) {
  if (tier == null) return 'Chọn gói dịch vụ.';
  if (addressId == null) return 'Chọn địa chỉ làm việc.';
  if (day == null) return 'Chọn ngày làm việc.';
  if (shiftCode == null) return 'Chọn ca làm việc.';
  if (!hasQuote) return 'Chưa có giá cho lựa chọn này.';
  if (needsTime && startTime == null) return 'Chọn giờ bắt đầu.';
  return null;
}

/// A failed order creation in words. The branch is the business CODE, never the server's message (booking.md 3.3).
class CreateOrderFailure {
  /// A message for the whole form; null when every problem belongs to a field.
  final String? message;

  /// Field name of the contract (`scheduledDate`, `shiftCode`, `customerNote`, `addressId`, `serviceTier`) -> message.
  final Map<String, String> fields;

  const CreateOrderFailure({this.message, this.fields = const {}});
}

CreateOrderFailure createOrderFailure(
  ApiException e, {
  int premiumMinLeadHours = 4,
}) {
  if (e.isNetworkError) return CreateOrderFailure(message: e.message);
  switch (e.statusCode) {
    case 400:
      final fields = <String, String>{
        for (final entry
            in (e.fieldErrors ?? const <String, List<String>>{}).entries)
          if (entry.value.isNotEmpty) entry.key: entry.value.first,
      };
      return fields.isEmpty
          ? CreateOrderFailure(
              message: e.message.isNotEmpty
                  ? e.message
                  : 'Thông tin đặt đơn không hợp lệ.',
            )
          : CreateOrderFailure(fields: fields);
    case 401:
      return const CreateOrderFailure(
        message: 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.',
      );
    case 403:
      return const CreateOrderFailure(
        message: 'Chỉ tài khoản khách hàng mới đặt được đơn.',
      );
    case 404:
      return const CreateOrderFailure(
        message: 'Không tìm thấy địa chỉ này trong sổ địa chỉ của bạn. Hãy chọn lại địa chỉ.',
      );
    case 409:
      return CreateOrderFailure(
        message: switch (e.code) {
          BookingErrorCode.shiftInPast =>
            'Ca này đã bắt đầu hoặc đã qua. Hãy chọn ca khác.',
          BookingErrorCode.premiumLeadTime =>
            'Gói Cao cấp cần đặt trước ít nhất $premiumMinLeadHours giờ. Chọn ca muộn hơn nhé.',
          BookingErrorCode.fullyBooked => 'Các doanh nghiệp đối tác đã kín lịch cho ca này. Hãy chọn ca hoặc ngày khác.',
          BookingErrorCode.leadTimeTooShort =>
            'Giờ này gần quá, người làm không kịp đến. Chọn giờ muộn hơn nhé.',
          BookingErrorCode.preferredWorkerBusy => 'Thợ bạn chọn đã bận giờ này. Chọn giờ khác hoặc để hệ thống chọn thợ.',
          _ => 'Không đặt được đơn lúc này. Vui lòng thử lại.',
        },
      );
    default:
      return const CreateOrderFailure(
        message: 'Máy chủ gặp lỗi. Vui lòng thử lại sau.',
      );
  }
}

/// A failed read (options, addresses, quote) in words.
String loadFailureMessage(ApiException e) {
  if (e.isNetworkError) return e.message;
  if (e.isUnauthorized) {
    return 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.';
  }
  if (e.isForbidden) {
    return 'Chỉ tài khoản khách hàng mới dùng được màn hình này.';
  }
  if (e.isNotFound) return 'Không tìm thấy dữ liệu.';
  return 'Máy chủ gặp lỗi. Vui lòng thử lại sau.';
}
