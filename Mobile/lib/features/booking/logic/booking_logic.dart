// Pure logic of the booking screens (no Flutter, no network): labels, VND, days in Asia/Ho_Chi_Minh, validation, error texts.
import '../../../core/network/api_client.dart';
import '../models/booking_models.dart';

/// The contract's business codes of POST /api/booking/orders (booking.md 3.3).
class BookingErrorCode {
  static const shiftInPast = 'SHIFT_IN_PAST';
  static const premiumLeadTime = 'PREMIUM_LEAD_TIME';
  static const fullyBooked = 'FULLY_BOOKED';

  BookingErrorCode._();
}

const int customerNoteMaxLength = 500;
const int requiredSkillMaxLength = 100;

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
  final text = rounded == rounded.roundToDouble() ? rounded.toStringAsFixed(0) : rounded.toString();
  return '${text.replaceAll('.', ',')} m²';
}

String tierLabel(String tier) => switch (tier) {
      ServiceTier.economy => 'Economy',
      ServiceTier.premium => 'Premium',
      _ => tier,
    };

/// One line that tells the two segments apart (PRD 2.1): who does the work and how it is assigned.
String tierDescription(String tier, BookingOptions options) => switch (tier) {
      ServiceTier.economy => 'Thợ tự do gần bạn nhận ca. Giá tiết kiệm.',
      ServiceTier.premium =>
        'Nhân viên của doanh nghiệp đối tác, có cam kết chất lượng. Cần đặt trước ít nhất ${options.premiumMinLeadHours} giờ.',
      _ => '',
    };

String shiftLabel(String shiftCode) => switch (shiftCode) {
      'SHIFT_MORNING' => 'Ca sáng',
      'SHIFT_AFTERNOON' => 'Ca chiều',
      'SHIFT_EVENING' => 'Ca tối',
      _ => shiftCode,
    };

String shiftText(ShiftOption shift) => '${shiftLabel(shift.shiftCode)} · ${shift.startLocal}–${shift.endLocal}';

/// The instant shown as a calendar day in Asia/Ho_Chi_Minh (UTC+7, no daylight saving).
DateTime hoChiMinhDay(DateTime instant) {
  final local = instant.toUtc().add(const Duration(hours: 7));
  return DateTime.utc(local.year, local.month, local.day);
}

/// The days the customer may pick: today (Ho Chi Minh) and the following [selectableDays] days.
List<DateTime> selectableDates(DateTime now) {
  final today = hoChiMinhDay(now);
  return [for (var i = 0; i <= selectableDays; i++) today.add(Duration(days: i))];
}

String _two(int n) => n.toString().padLeft(2, '0');

/// "yyyy-MM-dd", the `scheduledDate` of the contract.
String dateKey(DateTime day) => '${day.year}-${_two(day.month)}-${_two(day.day)}';

const _weekdays = ['Th 2', 'Th 3', 'Th 4', 'Th 5', 'Th 6', 'Th 7', 'CN'];

/// `Th 5 15/10`.
String dayLabel(DateTime day) => '${_weekdays[day.weekday - 1]} ${_two(day.day)}/${_two(day.month)}';

/// The line under the price: one worker, or two workers above the standard area (BR-02).
String workersNote(PriceQuote quote, BookingOptions options) => quote.requiredWorkers >= 2
    ? 'Diện tích ${formatArea(quote.totalAreaM2)} lớn hơn ${formatArea(options.standardMaxAreaM2)}: cần ${quote.requiredWorkers} thợ làm song song.'
    : '1 thợ, một ca tối đa ${options.shiftMaxHours} giờ.';

String? validateNote(String text) =>
    text.trim().length > customerNoteMaxLength ? 'Ghi chú tối đa $customerNoteMaxLength ký tự (đang ${text.trim().length}).' : null;

String? validateSkill(String text) =>
    text.trim().length > requiredSkillMaxLength ? 'Kỹ năng yêu cầu tối đa $requiredSkillMaxLength ký tự (đang ${text.trim().length}).' : null;

/// What is still missing before the order can be created; null when everything is chosen.
String? missingChoice({required String? tier, required int? addressId, required DateTime? day, required String? shiftCode, required bool hasQuote}) {
  if (tier == null) return 'Chọn phân khúc dịch vụ.';
  if (addressId == null) return 'Chọn địa chỉ làm việc.';
  if (day == null) return 'Chọn ngày làm việc.';
  if (shiftCode == null) return 'Chọn ca làm việc.';
  if (!hasQuote) return 'Chưa có giá cho lựa chọn này.';
  return null;
}

/// A failed order creation in words. The branch is the business CODE, never the server's message (booking.md 3.3).
class CreateOrderFailure {
  /// A message for the whole form; null when every problem belongs to a field.
  final String? message;

  /// Field name of the contract (`scheduledDate`, `shiftCode`, `customerNote`, `requiredSkill`, `addressId`, `serviceTier`) -> message.
  final Map<String, String> fields;

  const CreateOrderFailure({this.message, this.fields = const {}});
}

CreateOrderFailure createOrderFailure(ApiException e, {int premiumMinLeadHours = 4}) {
  if (e.isNetworkError) return CreateOrderFailure(message: e.message);
  switch (e.statusCode) {
    case 400:
      final fields = <String, String>{
        for (final entry in (e.fieldErrors ?? const <String, List<String>>{}).entries)
          if (entry.value.isNotEmpty) entry.key: entry.value.first,
      };
      return fields.isEmpty
          ? CreateOrderFailure(message: e.message.isNotEmpty ? e.message : 'Thông tin đặt đơn không hợp lệ.')
          : CreateOrderFailure(fields: fields);
    case 401:
      return const CreateOrderFailure(message: 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.');
    case 403:
      return const CreateOrderFailure(message: 'Chỉ tài khoản khách hàng mới đặt được đơn.');
    case 404:
      return const CreateOrderFailure(message: 'Không tìm thấy địa chỉ này trong sổ địa chỉ của bạn. Hãy chọn lại địa chỉ.');
    case 409:
      return CreateOrderFailure(
        message: switch (e.code) {
          BookingErrorCode.shiftInPast => 'Ca này đã bắt đầu hoặc đã qua. Hãy chọn ca khác.',
          BookingErrorCode.premiumLeadTime => 'Đơn Premium cần đặt trước ít nhất $premiumMinLeadHours giờ. Hãy chọn ca muộn hơn.',
          BookingErrorCode.fullyBooked => 'Các doanh nghiệp đối tác đã kín lịch cho ca này. Hãy chọn ca hoặc ngày khác.',
          _ => 'Không đặt được đơn lúc này. Vui lòng thử lại.',
        },
      );
    default:
      return const CreateOrderFailure(message: 'Máy chủ gặp lỗi. Vui lòng thử lại sau.');
  }
}

/// A failed read (options, addresses, quote) in words.
String loadFailureMessage(ApiException e) {
  if (e.isNetworkError) return e.message;
  if (e.isUnauthorized) return 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.';
  if (e.isForbidden) return 'Chỉ tài khoản khách hàng mới dùng được màn hình này.';
  if (e.isNotFound) return 'Không tìm thấy dữ liệu.';
  return 'Máy chủ gặp lỗi. Vui lòng thử lại sau.';
}
