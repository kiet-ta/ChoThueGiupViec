import '../../../core/network/api_client.dart';
import '../models/payout_models.dart';

// Pure helpers of the worker income screens: months, display formats, labels and error texts.

/// Ho Chi Minh time is UTC+7 all year (no daylight saving), decision G-3.
const Duration vietnamOffset = Duration(hours: 7);

/// The month selector never goes back before this year (the platform did not exist earlier).
const int firstSelectableYear = 2024;

/// `YYYY-MM` of the Ho Chi Minh calendar for an instant.
String monthKeyOf(DateTime instant) {
  final local = instant.toUtc().add(vietnamOffset);
  return '${local.year.toString().padLeft(4, '0')}-${local.month.toString().padLeft(2, '0')}';
}

/// Moves a `YYYY-MM` key by [delta] months across year boundaries.
String shiftMonth(String key, int delta) {
  final year = int.parse(key.substring(0, 4));
  final month = int.parse(key.substring(5, 7));
  final index = year * 12 + (month - 1) + delta;
  return '${(index ~/ 12).toString().padLeft(4, '0')}-${(index % 12 + 1).toString().padLeft(2, '0')}';
}

/// The server answers 400 for a future month, so the selector never goes past the current one.
bool canGoNext(String key, String currentKey) => key.compareTo(currentKey) < 0;

/// January of [firstSelectableYear] is the first month.
bool canGoPrevious(String key) => key.compareTo('$firstSelectableYear-01') > 0;

/// "Tháng 10/2026".
String monthLabel(String key) => 'Tháng ${int.parse(key.substring(5, 7))}/${key.substring(0, 4)}';

/// Whole VND with dots between thousands: `1.234.567 đ` (decision G-2).
String formatVnd(int amount) {
  final digits = amount.abs().toString();
  final grouped = StringBuffer();
  for (var i = 0; i < digits.length; i++) {
    if (i > 0 && (digits.length - i) % 3 == 0) grouped.write('.');
    grouped.write(digits[i]);
  }
  return '${amount < 0 ? '-' : ''}$grouped đ';
}

/// An instant shown as Ho Chi Minh time: `01/10/2026 07:30` (decision G-3). Null shows a dash.
String formatVietnamDateTime(DateTime? instant) {
  if (instant == null) return '—';
  final l = instant.toUtc().add(vietnamOffset);
  String two(int n) => n.toString().padLeft(2, '0');
  return '${two(l.day)}/${two(l.month)}/${l.year} ${two(l.hour)}:${two(l.minute)}';
}

/// The date part of [formatVietnamDateTime]: `01/10/2026`.
String formatVietnamDate(DateTime? instant) {
  if (instant == null) return '—';
  return formatVietnamDateTime(instant).substring(0, 10);
}

/// What the worker reads for the state of the month's payout.
String payoutStatusLabel(String status) {
  switch (status) {
    case PayoutStatus.pending:
      return 'Chờ chuyển khoản';
    case PayoutStatus.transferred:
      return 'Đã chuyển khoản';
    default:
      return 'Chưa chốt đợt';
  }
}

/// A short explanation under the status, because pay is monthly and held until a batch (Q04).
String payoutStatusHint(String status) {
  switch (status) {
    case PayoutStatus.pending:
      return 'Đợt giải ngân đã được lập, đang chờ Admin chuyển khoản.';
    case PayoutStatus.transferred:
      return 'Khoản này đã được chuyển vào tài khoản của bạn.';
    default:
      return 'Thu nhập được giải ngân theo tháng, sau khi tháng kết thúc.';
  }
}

/// What a failed load is told to the worker. 403 is an agency staff member: the agency is paid, not the worker (PRD 4.4).
String earningsFailureMessage(ApiException e) {
  if (e.isForbidden) {
    return 'Bạn làm việc cho một đại lý nên thu nhập được trả cho đại lý, không trả trực tiếp cho bạn.';
  }
  if (e.isNotFound) return 'Không tìm thấy dữ liệu thu nhập.';
  if (e.statusCode == 400) return 'Tháng đã chọn không hợp lệ.';
  return e.message;
}
