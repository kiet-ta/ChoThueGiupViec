import '../../../core/network/api_client.dart';
import '../services/worker_bank_service.dart';
import 'payout_logic.dart';
import 'wallet_logic.dart';

// Pure helpers of the worker's payout account screen: the field rules (the same as the server's WorkerBankValidator),
// the quick picks and the texts.

const int bankNameMaxLength = 100;
const int accountMinLength = 6;
const int accountMaxLength = 30;

/// Said on the screen: the account is a test one, no real transfer is made (decision G-1).
const String bankSandboxNote =
    'Đây là tài khoản ảo để thử nghiệm: hệ thống không chuyển tiền thật. Admin dùng thông tin này để đối chiếu khoản rút và lương tháng của bạn.';

/// Said before a change request is sent, and again in the confirmation (decided with Duy: 15 days from the day it is sent).
const String bankChangeWarning =
    'Sau khi gửi đơn, 15 ngày không đổi được nữa. Trong lúc chờ Admin duyệt, các khoản rút và lương vẫn dùng tài khoản hiện tại.';

/// The state of the screen, from the server's answer: which of the four things the worker can do.
enum BankStage {
  /// No account yet: the form saves the first one at once.
  firstAccount,

  /// An account and no lock: the worker may send a change request.
  canRequest,

  /// A request waits for the Admin.
  pending,

  /// A request was sent less than 15 days ago (approved or rejected).
  locked,
}

BankStage bankStage(WorkerBank bank) {
  if (!bank.isSet) return BankStage.firstAccount;
  if (bank.pendingRequest != null) return BankStage.pending;
  if (bank.canRequestChange) return BankStage.canRequest;
  return BankStage.locked;
}

/// "Bạn đã gửi đơn đổi tài khoản ngày 12/11/2026. Có thể gửi đơn mới từ ngày 27/11/2026." (Vietnam dates); the sent date is left out when unknown.
String bankLockNote(BankChangeLast? last, DateTime? lockedUntil) {
  final sent = last?.requestedAt;
  final from = lockedUntil == null
      ? ''
      : ' Có thể gửi đơn mới từ ngày ${formatVietnamDate(lockedUntil)}.';
  final head = sent == null
      ? 'Bạn đã gửi đơn đổi tài khoản gần đây.'
      : 'Bạn đã gửi đơn đổi tài khoản ngày ${formatVietnamDate(sent)}.';
  return '$head$from';
}

/// What the worker reads about the latest decision while locked; null when there is nothing to add.
String? bankLastDecisionNote(BankChangeLast? last) {
  if (last == null) return null;
  if (last.isRejected) {
    final reason = last.rejectReason?.trim();
    return reason == null || reason.isEmpty
        ? 'Đơn gần nhất bị từ chối.'
        : 'Đơn gần nhất bị từ chối: $reason';
  }
  if (last.isApproved) {
    return 'Đơn gần nhất đã được duyệt: tài khoản mới đang được dùng.';
  }
  return null;
}

/// "Đơn đổi tài khoản đã gửi ngày 12/11/2026, đang chờ Admin duyệt."
String bankPendingNote(BankChangePending pending) {
  final sent = pending.requestedAt;
  return sent == null
      ? 'Đơn đổi tài khoản đang chờ Admin duyệt.'
      : 'Đơn đổi tài khoản đã gửi ngày ${formatVietnamDate(sent)}, đang chờ Admin duyệt.';
}

/// Quick picks under the bank name field.
const List<String> commonBanks = [
  'Vietcombank',
  'ACB',
  'Techcombank',
  'MB Bank',
  'BIDV',
  'VPBank',
];

/// The digits of an account number the worker typed: spaces are dropped, like the server does.
String normalizeAccount(String text) => text.replaceAll(RegExp(r'\s'), '');

/// The message under the bank name field, or null when it can be sent.
String? bankNameError(String text) {
  final name = text.trim();
  if (name.isEmpty) return 'Hãy nhập tên ngân hàng. Ví dụ: Vietcombank';
  if (name.length > bankNameMaxLength) {
    return 'Tên ngân hàng tối đa $bankNameMaxLength ký tự.';
  }
  return null;
}

/// The message under the account number field, or null when it can be sent.
String? accountError(String text) {
  final account = normalizeAccount(text);
  if (account.isEmpty) return 'Hãy nhập số tài khoản. Ví dụ: 0011001234567';
  final digitsOnly = RegExp(r'^\d+$').hasMatch(account);
  if (!digitsOnly ||
      account.length < accountMinLength ||
      account.length > accountMaxLength) {
    return 'Số tài khoản gồm $accountMinLength đến $accountMaxLength chữ số.';
  }
  return null;
}

/// "•••• 4567": the last four digits of an account, so the full number is not shown where it is not needed.
String maskedAccount(String account) {
  final digits = normalizeAccount(account);
  if (digits.isEmpty) return '—';
  final tail = digits.length <= 4
      ? digits
      : digits.substring(digits.length - 4);
  return '•••• $tail';
}

/// What a failed load or save is told to the worker. A 400 shows the server's message for the field it names.
String bankFailureMessage(ApiException e) {
  if (e.isForbidden) return walletFailureMessage(e);
  switch (e.code) {
    case 'BANK_CHANGE_REQUIRES_REQUEST':
      return 'Bạn đã có tài khoản nhận tiền. Muốn đổi, hãy gửi đơn đổi tài khoản để Admin duyệt.';
    case 'BANK_CHANGE_PENDING':
      return 'Bạn đã có một đơn đổi tài khoản đang chờ Admin duyệt.';
    case 'BANK_NOT_SET':
      return 'Bạn chưa có tài khoản nhận tiền: hãy thêm tài khoản, không cần gửi đơn.';
    case 'BANK_CHANGE_LOCKED':
      // the server's message names the date from which a new request can be sent
      return e.message;
  }
  if (e.statusCode == 400) {
    final fields = e.fieldErrors;
    for (final key in const ['bankName', 'bankAccountNo']) {
      final messages = fields?[key];
      if (messages != null && messages.isNotEmpty) return messages.first;
    }
    return 'Thông tin tài khoản chưa đúng. Hãy kiểm tra lại tên ngân hàng và số tài khoản.';
  }
  return e.message;
}
