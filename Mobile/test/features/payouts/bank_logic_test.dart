import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/payouts/logic/bank_logic.dart';
import 'package:mobile/features/payouts/services/worker_bank_service.dart';

void main() {
  group('model', () {
    test('reads the answer of the endpoint', () {
      final b = WorkerBank.fromJson({
        'bankName': 'ACB',
        'bankAccountNo': '0011001234567',
        'isSet': true,
      });
      expect(
        [b.bankName, b.bankAccountNo, b.isSet],
        ['ACB', '0011001234567', true],
      );
    });

    test('an account that was never set has empty strings and is not set', () {
      final b = WorkerBank.fromJson({
        'bankName': '',
        'bankAccountNo': '',
        'isSet': false,
      });
      expect([b.bankName, b.bankAccountNo, b.isSet], ['', '', false]);
      expect(WorkerBank.fromJson({}).isSet, isFalse);
    });
  });

  group('bank name', () {
    test('is required and at most 100 characters after trimming', () {
      expect(bankNameError(''), contains('Hãy nhập tên ngân hàng'));
      expect(bankNameError('   '), contains('Hãy nhập tên ngân hàng'));
      expect(bankNameError('  ACB  '), isNull);
      expect(bankNameError('x' * 100), isNull);
      expect(bankNameError('x' * 101), 'Tên ngân hàng tối đa 100 ký tự.');
    });

    test('offers the common banks as quick picks', () {
      expect(commonBanks, containsAll(['Vietcombank', 'ACB', 'BIDV']));
    });
  });

  group('account number', () {
    test('drops spaces and needs 6 to 30 digits', () {
      expect(
        normalizeAccount(' 0011 0012 3456 7 '),
        '00110012345 67'.replaceAll(' ', ''),
      );
      expect(accountError('001100123456'), isNull);
      expect(accountError('0011 0012 3456'), isNull);
      expect(accountError('123456'), isNull);
      expect(accountError('1' * 30), isNull);
    });

    test('refuses an empty, short, long or non-digit number', () {
      expect(accountError(''), contains('Hãy nhập số tài khoản'));
      expect(accountError('   '), contains('Hãy nhập số tài khoản'));
      const range = 'Số tài khoản gồm 6 đến 30 chữ số.';
      expect(accountError('12345'), range);
      expect(accountError('1' * 31), range);
      expect(accountError('12345a'), range);
      expect(accountError('12-3456'), range);
    });

    test('shows only the last four digits where the number is not needed', () {
      expect(maskedAccount('0011001234567'), '•••• 4567');
      expect(maskedAccount('0011 0012 3456'), '•••• 3456');
      expect(maskedAccount('123'), '•••• 123');
      expect(maskedAccount(''), '—');
    });
  });

  group('errors', () {
    test('shows the server message of the field a 400 names', () {
      expect(
        bankFailureMessage(
          const ApiException(
            400,
            'x',
            fieldErrors: {
              'bankAccountNo': ['Bank account number must be 6 to 30 digits.'],
            },
          ),
        ),
        'Bank account number must be 6 to 30 digits.',
      );
      expect(
        bankFailureMessage(
          const ApiException(
            400,
            'x',
            fieldErrors: {
              'bankName': ['Bank name is required.'],
            },
          ),
        ),
        'Bank name is required.',
      );
    });

    test('explains a 400 without field messages, a 403 and other failures', () {
      expect(
        bankFailureMessage(const ApiException(400, 'x')),
        contains('chưa đúng'),
      );
      expect(
        bankFailureMessage(const ApiException(403, 'x')),
        contains('không có số dư riêng'),
      );
      expect(bankFailureMessage(const ApiException(500, 'boom')), 'boom');
    });

    test('says that the account is a test one', () {
      expect(bankSandboxNote, contains('tài khoản ảo'));
      expect(bankSandboxNote, contains('không chuyển tiền thật'));
    });
  });
}
