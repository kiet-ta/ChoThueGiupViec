import '../../../core/network/api_client.dart';
import '../models/wallet_models.dart';
import 'payout_logic.dart';

// Pure helpers of the worker wallet screen: the amount field, the labels and the error texts.

/// The line that is always shown above the form (acceptance criterion 2 of LEDGER-16, decision E).
const String withdrawalSingleRequestWarning =
    'Mỗi lần bạn chỉ có thể có 1 yêu cầu rút đang chờ duyệt. Hãy chờ duyệt xong hoặc huỷ yêu cầu cũ trước khi gửi yêu cầu mới.';

/// Said under the warning: the money is an advance on the month, not a bank transfer.
const String withdrawalAdvanceHint =
    'Khi được duyệt, số tiền rút được trừ vào lương tháng này. Hệ thống không chuyển tiền thật.';

const String _amountExample = 'Số tiền chưa đúng. Ví dụ: 100.000';

/// Reads the amount typed by the worker: whole VND, with dots, commas or spaces between thousands allowed.
/// Null for an empty field, a letter, a decimal or zero.
int? parseWithdrawalAmount(String text) {
  final cleaned = text.trim().replaceAll(RegExp(r'[.,\s]'), '');
  if (cleaned.isEmpty || !RegExp(r'^\d+$').hasMatch(cleaned)) return null;
  final value = int.tryParse(cleaned);
  if (value == null || value <= 0) return null;
  return value;
}

/// The message under the field, or null when the amount can be sent. [available] is what the server says can be withdrawn.
String? withdrawalAmountError(String text, int available) {
  final amount = parseWithdrawalAmount(text);
  if (amount == null) return _amountExample;
  if (available <= 0) return 'Hiện chưa có tiền có thể rút.';
  if (amount > available) {
    return 'Bạn chỉ có thể rút tối đa ${formatVnd(available)}.';
  }
  return null;
}

/// The form is locked while a request waits for the Admin (the server allows only one).
bool canRequestWithdrawal(Wallet wallet) =>
    wallet.pendingWithdrawal == null && wallet.available > 0;

/// What the worker reads for the state of a request.
String withdrawalStatusLabel(String status) {
  switch (status) {
    case WithdrawalStatus.pending:
      return 'Chờ duyệt';
    case WithdrawalStatus.paid:
      return 'Đã duyệt';
    case WithdrawalStatus.rejected:
      return 'Bị từ chối';
    case WithdrawalStatus.cancelled:
      return 'Đã huỷ';
    default:
      return status;
  }
}

/// "Một phần tiền sẽ có thể rút từ 12/11/2026 09:30.", or null when nothing is waiting.
String? nextAvailableText(DateTime? nextAvailableAt) {
  if (nextAvailableAt == null) return null;
  return 'Một phần tiền sẽ có thể rút từ ${formatVietnamDateTime(nextAvailableAt)}.';
}

/// What a failed load of the wallet is told to the worker. 403: an agency worker is paid through the agency.
String walletFailureMessage(ApiException e) {
  if (e.isForbidden) {
    return 'Bạn làm việc cho một đại lý nên tiền công được trả cho đại lý, bạn không có ví riêng.';
  }
  if (e.isNotFound) return 'Không tìm thấy ví của bạn.';
  return e.message;
}

/// What a failed request to withdraw or to cancel is told to the worker; the 409 codes of the contract are translated.
String withdrawalFailureMessage(ApiException e) {
  if (e.isConflict) {
    switch (e.code) {
      case 'WITHDRAWAL_PENDING':
        return 'Bạn đang có một yêu cầu chờ duyệt. Hãy chờ duyệt xong hoặc huỷ yêu cầu đó trước.';
      case 'WITHDRAWAL_NOT_PENDING':
        return 'Yêu cầu này đã được xử lý nên không huỷ được nữa.';
      case 'WITHDRAWAL_INSUFFICIENT':
        return 'Số tiền có thể rút đã thay đổi. Hãy kiểm tra lại số dư.';
      default:
        return e.message;
    }
  }
  if (e.statusCode == 400) {
    final messages = e.fieldErrors?['amount'];
    return messages != null && messages.isNotEmpty
        ? messages.first
        : _amountExample;
  }
  if (e.isForbidden) return walletFailureMessage(e);
  if (e.isNotFound) return 'Không tìm thấy yêu cầu này.';
  return e.message;
}

/// After these the screen's figures are stale (another device or the Admin changed the request), so it loads the wallet again.
bool shouldReloadWalletAfter(ApiException e) => e.isConflict || e.isNotFound;
