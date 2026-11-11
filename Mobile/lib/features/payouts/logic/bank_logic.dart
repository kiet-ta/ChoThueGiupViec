import '../../../core/network/api_client.dart';
import 'wallet_logic.dart';

// Pure helpers of the worker's payout account screen: the field rules (the same as the server's WorkerBankValidator),
// the quick picks and the texts.

const int bankNameMaxLength = 100;
const int accountMinLength = 6;
const int accountMaxLength = 30;

/// Said on the screen: the account is a test one, no real transfer is made (decision G-1).
const String bankSandboxNote =
    'Đây là tài khoản ảo để thử nghiệm: hệ thống không chuyển tiền thật. Admin dùng thông tin này để đối chiếu khoản rút và lương tháng của bạn.';

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
