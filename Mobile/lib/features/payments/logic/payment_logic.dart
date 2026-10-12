// Pure logic of the payment screen (no Flutter, no network): what to show for a payment at a given instant, and error texts.
import '../../../core/network/api_client.dart';
import '../models/payment_models.dart';

/// The contract's business codes of the QR endpoints (payments.md 2.1, 2.2).
class PaymentErrorCode {
  static const invalidState = 'INVALID_STATE';
  static const paymentExpired = 'PAYMENT_EXPIRED';

  PaymentErrorCode._();
}

/// The banner every payment screen carries (decision Q04): no real money moves in this product.
const String sandboxBannerText = 'SANDBOX – no real money';

/// How often the screen asks the server for the payment status (the contract's polling fallback, payments.md 2.3).
const Duration defaultPollInterval = Duration(seconds: 3);

/// What the customer is looking at.
enum PaymentPhase {
  /// Waiting for the payment; the QR is valid.
  waiting,

  /// The server says the money arrived.
  paid,

  /// The QR can no longer be paid (the server says EXPIRED, or its deadline passed).
  expired,

  /// The money was given back.
  refunded,
}

PaymentPhase phaseOf(Payment payment, DateTime now) {
  switch (payment.txnStatus) {
    case PaymentTxnStatus.success:
      return PaymentPhase.paid;
    case PaymentTxnStatus.refunded:
      return PaymentPhase.refunded;
    case PaymentTxnStatus.expired:
      return PaymentPhase.expired;
  }
  final deadline = payment.expiresAt;
  // The deadline is the server's; once it has passed locally the QR is no longer offered (the server expires it itself).
  if (deadline != null && !now.toUtc().isBefore(deadline)) return PaymentPhase.expired;
  return PaymentPhase.waiting;
}

/// True while asking the server again can still change what is shown.
bool shouldKeepPolling(Payment payment, DateTime now) => phaseOf(payment, now) == PaymentPhase.waiting;

/// Time left until the deadline, never negative; zero when there is no deadline.
Duration timeLeft(Payment payment, DateTime now) {
  final deadline = payment.expiresAt;
  if (deadline == null) return Duration.zero;
  final left = deadline.difference(now.toUtc());
  return left.isNegative ? Duration.zero : left;
}

/// `14:59`.
String countdownText(Duration left) {
  final minutes = left.inMinutes;
  final seconds = left.inSeconds % 60;
  return '${minutes.toString().padLeft(2, '0')}:${seconds.toString().padLeft(2, '0')}';
}

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

String purposeLabel(Payment payment) => payment.purpose == 'EXTENSION' ? 'Thanh toán làm thêm giờ' : 'Thanh toán đơn';

/// A failed "create the QR" call in words. The branch is the business CODE, never the server's message (payments.md 2.1).
String qrFailureMessage(ApiException e) {
  if (e.isNetworkError) return e.message;
  switch (e.statusCode) {
    case 401:
      return 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.';
    case 403:
      return 'Chỉ tài khoản khách hàng mới thanh toán được.';
    case 404:
      return 'Không tìm thấy đơn này trong tài khoản của bạn.';
    case 409:
      return switch (e.code) {
        PaymentErrorCode.paymentExpired => 'Đã quá hạn thanh toán. Đơn sẽ được huỷ và bạn không bị trừ tiền.',
        PaymentErrorCode.invalidState => 'Đơn này không còn chờ thanh toán (đã thanh toán hoặc đã huỷ).',
        _ => 'Không tạo được mã thanh toán lúc này.',
      };
    case 502:
      return 'Cổng thanh toán chưa phản hồi. Chưa có gì bị trừ, bạn có thể thử lại.';
    default:
      return 'Máy chủ gặp lỗi. Vui lòng thử lại sau.';
  }
}

/// A failed status check is not shown as an error on its own (the next poll may work); this is the text after several in a row.
const String pollTroubleMessage = 'Chưa kiểm tra được trạng thái thanh toán. Đang thử lại…';
