import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/payouts/logic/wallet_logic.dart';
import 'package:mobile/features/payouts/models/wallet_models.dart';

Wallet wallet({
  int balance = 288000,
  int available = 188000,
  int held = 100000,
  PendingWithdrawal? pending,
  DateTime? next,
  bool paused = false,
}) => Wallet(
  balance: balance,
  available: available,
  held: held,
  pendingWithdrawal: pending,
  nextAvailableAt: next,
  withdrawalsPaused: paused,
);

const pendingRequest = PendingWithdrawal(
  withdrawalId: 7,
  amount: 100000,
  status: 'PENDING',
  requestedAt: null,
);

void main() {
  group('wallet models', () {
    test('read the example answer of the contract', () {
      final w = Wallet.fromJson({
        'balance': 288000,
        'available': 188000,
        'held': 100000,
        'pendingWithdrawal': {
          'withdrawalId': 7,
          'amount': 100000,
          'status': 'PENDING',
          'requestedAt': '2026-11-05T02:00:00Z',
        },
        'nextAvailableAt': null,
      });

      expect([w.balance, w.available, w.held], [288000, 188000, 100000]);
      expect(w.pendingWithdrawal!.withdrawalId, 7);
      expect(w.pendingWithdrawal!.requestedAt, DateTime.utc(2026, 11, 5, 2));
      expect(w.nextAvailableAt, isNull);
    });

    test('tolerate missing fields and a wallet without a request', () {
      final w = Wallet.fromJson({});
      expect([w.balance, w.available, w.held], [0, 0, 0]);
      expect(w.pendingWithdrawal, isNull);
      expect(w.withdrawalsPaused, isFalse);
    });

    test('LEDGER-30: read withdrawalsPaused', () {
      expect(
        Wallet.fromJson({'withdrawalsPaused': true}).withdrawalsPaused,
        isTrue,
      );
      expect(
        Wallet.fromJson({'withdrawalsPaused': false}).withdrawalsPaused,
        isFalse,
      );
    });

    test('read a withdrawal answer with its reject reason', () {
      final x = Withdrawal.fromJson({
        'withdrawalId': 9,
        'amount': 50000,
        'status': 'REJECTED',
        'requestedAt': '2026-11-05T02:00:00Z',
        'reviewedAt': '2026-11-05T03:00:00Z',
        'rejectReason': 'Thiếu chứng từ',
      });
      expect([x.withdrawalId, x.amount, x.status], [9, 50000, 'REJECTED']);
      expect(x.rejectReason, 'Thiếu chứng từ');
      expect(x.reviewedAt, DateTime.utc(2026, 11, 5, 3));
    });
  });

  group('amount field', () {
    test('parses whole VND with separators and refuses the rest', () {
      expect(parseWithdrawalAmount('100000'), 100000);
      expect(parseWithdrawalAmount(' 100.000 '), 100000);
      expect(parseWithdrawalAmount('1,200,000'), 1200000);
      expect(parseWithdrawalAmount('1 200 000'), 1200000);
      expect(parseWithdrawalAmount(''), isNull);
      expect(parseWithdrawalAmount('abc'), isNull);
      expect(parseWithdrawalAmount('10a'), isNull);
      expect(parseWithdrawalAmount('0'), isNull);
      expect(parseWithdrawalAmount('-5000'), isNull);
    });

    test('checks the number against what the server says is withdrawable', () {
      expect(withdrawalAmountError('100.000', 188000), isNull);
      expect(withdrawalAmountError('188000', 188000), isNull);
      expect(
        withdrawalAmountError('188001', 188000),
        'Bạn chỉ có thể rút tối đa 188.000 đ.',
      );
      expect(withdrawalAmountError('', 188000), contains('Ví dụ: 100.000'));
      expect(withdrawalAmountError('abc', 188000), contains('Ví dụ'));
      expect(withdrawalAmountError('1000', 0), 'Hiện chưa có tiền có thể rút.');
    });
  });

  group('lock', () {
    test(
      'blocks a second request while one is pending or nothing is withdrawable',
      () {
        expect(canRequestWithdrawal(wallet()), isTrue);
        expect(canRequestWithdrawal(wallet(pending: pendingRequest)), isFalse);
        expect(canRequestWithdrawal(wallet(available: 0)), isFalse);
        expect(
          canRequestWithdrawal(wallet(paused: true)),
          isFalse,
        ); // LEDGER-30
      },
    );

    test('says plainly that only one request can wait', () {
      expect(withdrawalSingleRequestWarning, contains('1 yêu cầu rút'));
      expect(withdrawalAdvanceHint, contains('trừ vào lương tháng'));
    });
  });

  group('labels', () {
    test('name each status in words', () {
      expect(withdrawalStatusLabel('PENDING'), 'Chờ duyệt');
      expect(withdrawalStatusLabel('PAID'), 'Đã duyệt');
      expect(withdrawalStatusLabel('REJECTED'), 'Bị từ chối');
      expect(withdrawalStatusLabel('CANCELLED'), 'Đã huỷ');
      expect(withdrawalStatusLabel('X'), 'X');
    });

    test(
      'shows the next time in Ho Chi Minh time, nothing when none is waiting',
      () {
        expect(nextAvailableText(null), isNull);
        expect(
          nextAvailableText(DateTime.utc(2026, 11, 12, 2, 30)),
          'Một phần tiền sẽ có thể rút từ 12/11/2026 09:30.',
        );
      },
    );
  });

  group('errors', () {
    test('translate the 409 codes of the contract', () {
      ApiException conflict(String? code) =>
          ApiException(409, 'raw', code: code);
      expect(
        withdrawalFailureMessage(conflict('WITHDRAWAL_PENDING')),
        contains('đang có một yêu cầu chờ duyệt'),
      );
      expect(
        withdrawalFailureMessage(conflict('WITHDRAWAL_NOT_PENDING')),
        contains('đã được xử lý'),
      );
      expect(
        withdrawalFailureMessage(conflict('WITHDRAWAL_INSUFFICIENT')),
        contains('đã thay đổi'),
      );
      expect(
        withdrawalFailureMessage(conflict('WITHDRAWAL_SETTLING')),
        withdrawalsPausedText,
      );
      expect(withdrawalFailureMessage(conflict('OTHER')), 'raw');
      expect(withdrawalFailureMessage(conflict(null)), 'raw');
    });

    test(
      'use the server field message for a bad amount and explain the rest',
      () {
        expect(
          withdrawalFailureMessage(
            const ApiException(
              400,
              'x',
              fieldErrors: {
                'amount': ['Số tiền vượt quá số có thể rút.'],
              },
            ),
          ),
          'Số tiền vượt quá số có thể rút.',
        );
        expect(
          withdrawalFailureMessage(const ApiException(400, 'x')),
          contains('Ví dụ: 100.000'),
        );
        expect(
          withdrawalFailureMessage(const ApiException(403, 'x')),
          contains('đại lý'),
        );
        expect(
          withdrawalFailureMessage(const ApiException(404, 'x')),
          'Không tìm thấy yêu cầu này.',
        );
        expect(
          withdrawalFailureMessage(const ApiException(500, 'Lỗi máy chủ')),
          'Lỗi máy chủ',
        );
      },
    );

    test('tell an agency worker why there is no wallet and reload after a conflict', () {
      expect(
        walletFailureMessage(const ApiException(403, 'x')),
        contains('không có số dư riêng'),
      );
      expect(walletFailureMessage(const ApiException(500, 'boom')), 'boom');
      expect(shouldReloadWalletAfter(const ApiException(409, 'x')), isTrue);
      expect(shouldReloadWalletAfter(const ApiException(404, 'x')), isTrue);
      expect(shouldReloadWalletAfter(const ApiException(400, 'x')), isFalse);
      expect(shouldReloadWalletAfter(const ApiException(0, 'x')), isFalse);
    });
  });
}
