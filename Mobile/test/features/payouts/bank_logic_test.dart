import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/payouts/logic/bank_logic.dart';
import 'package:mobile/features/payouts/services/worker_bank_service.dart';

void main() {
  group('the change request (LEDGER-28, MOB-BANK-01)', () {
    final sent = DateTime.utc(2026, 11, 12, 18, 10); // 13/11/2026 in Vietnam
    final until = DateTime.utc(2026, 11, 27, 18, 10); // 28/11/2026 in Vietnam

    test(
      'the model reads the pending request, the lock and the latest request',
      () {
        final b = WorkerBank.fromJson({
          'bankName': 'ACB',
          'bankAccountNo': '0011001234567',
          'isSet': true,
          'canRequestChange': false,
          'changeLockedUntil': '2026-11-27T18:10:00Z',
          'pendingRequest': {
            'requestId': 4,
            'bankName': 'VCB',
            'bankAccountNo': '123456',
            'requestedAt': '2026-11-12T18:10:00Z',
          },
          'lastRequest': {
            'status': 'REJECTED',
            'rejectReason': 'Sai tên',
            'requestedAt': '2026-11-12T18:10:00Z',
            'reviewedAt': null,
          },
        });

        expect(b.canRequestChange, isFalse);
        expect(b.changeLockedUntil, until);
        expect(b.pendingRequest?.requestId, 4);
        expect(b.lastRequest?.isRejected, isTrue);
        expect(b.lastRequest?.rejectReason, 'Sai tên');
      },
    );

    test('an old answer without the new fields reads as "cannot request", no lock, no requests', () {
      final b = WorkerBank.fromJson({
        'bankName': 'ACB',
        'bankAccountNo': '1',
        'isSet': true,
      });

      expect(b.canRequestChange, isFalse);
      expect(b.changeLockedUntil, isNull);
      expect(b.pendingRequest, isNull);
      expect(b.lastRequest, isNull);
    });

    test('the stage: first account, can request, pending, locked', () {
      WorkerBank bank({
        bool set = true,
        bool can = false,
        bool pending = false,
      }) => WorkerBank(
        bankName: set ? 'ACB' : '',
        bankAccountNo: set ? '123456' : '',
        isSet: set,
        canRequestChange: can,
        pendingRequest: pending
            ? BankChangePending(requestId: 1, bankName: 'V', bankAccountNo: '1')
            : null,
      );

      expect(bankStage(bank(set: false)), BankStage.firstAccount);
      expect(bankStage(bank(can: true)), BankStage.canRequest);
      expect(bankStage(bank(pending: true)), BankStage.pending);
      expect(bankStage(bank()), BankStage.locked);
    });

    test('the lock note names the sent day and the day a new request can be sent, in Vietnam dates', () {
      expect(
        bankLockNote(
          BankChangeLast(status: 'APPROVED', requestedAt: sent),
          until,
        ),
        'Bạn đã gửi đơn đổi tài khoản ngày 13/11/2026. Có thể gửi đơn mới từ ngày 28/11/2026.',
      );
      expect(
        bankLockNote(null, until),
        'Bạn đã gửi đơn đổi tài khoản gần đây. Có thể gửi đơn mới từ ngày 28/11/2026.',
      );
      expect(
        bankLockNote(
          BankChangeLast(status: 'APPROVED', requestedAt: sent),
          null,
        ),
        'Bạn đã gửi đơn đổi tài khoản ngày 13/11/2026.',
      );
    });

    test('the last decision: the reason of a rejection, an approval, nothing for a pending one', () {
      expect(
        bankLastDecisionNote(
          BankChangeLast(status: 'REJECTED', rejectReason: ' Sai tên '),
        ),
        'Đơn gần nhất bị từ chối: Sai tên',
      );
      expect(
        bankLastDecisionNote(BankChangeLast(status: 'REJECTED')),
        'Đơn gần nhất bị từ chối.',
      );
      expect(
        bankLastDecisionNote(BankChangeLast(status: 'APPROVED')),
        contains('đã được duyệt'),
      );
      expect(bankLastDecisionNote(BankChangeLast(status: 'PENDING')), isNull);
      expect(bankLastDecisionNote(null), isNull);
    });

    test('the pending note names the day it was sent', () {
      expect(
        bankPendingNote(
          BankChangePending(
            requestId: 1,
            bankName: 'V',
            bankAccountNo: '1',
            requestedAt: sent,
          ),
        ),
        'Đơn đổi tài khoản đã gửi ngày 13/11/2026, đang chờ Admin duyệt.',
      );
      expect(
        bankPendingNote(
          BankChangePending(requestId: 1, bankName: 'V', bankAccountNo: '1'),
        ),
        'Đơn đổi tài khoản đang chờ Admin duyệt.',
      );
    });

    test(
      'the warning says 15 days and that withdrawals keep the current account',
      () {
        expect(bankChangeWarning, contains('15 ngày không đổi được nữa'));
        expect(bankChangeWarning, contains('tài khoản hiện tại'));
      },
    );
  });

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

    test('a 409 of the change flow is told in plain words, the lock with the server message', () {
      String say(String code, [String message = 'x']) =>
          bankFailureMessage(ApiException(409, message, code: code));

      expect(
        say('BANK_CHANGE_REQUIRES_REQUEST'),
        contains('gửi đơn đổi tài khoản'),
      );
      expect(say('BANK_CHANGE_PENDING'), contains('đang chờ Admin duyệt'));
      expect(say('BANK_NOT_SET'), contains('không cần gửi đơn'));
      expect(
        say('BANK_CHANGE_LOCKED', 'Có thể gửi đơn mới từ ngày 28/11/2026.'),
        'Có thể gửi đơn mới từ ngày 28/11/2026.',
      );
    });

    test('says that the account is a test one', () {
      expect(bankSandboxNote, contains('tài khoản ảo'));
      expect(bankSandboxNote, contains('không chuyển tiền thật'));
    });
  });
}
