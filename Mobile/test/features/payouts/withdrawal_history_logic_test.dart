import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/payouts/logic/withdrawal_history_logic.dart';
import 'package:mobile/features/payouts/models/wallet_models.dart';
import 'package:mobile/features/payouts/services/withdrawal_history_service.dart';

Withdrawal request({
  int id = 7,
  int amount = 100000,
  String status = 'PENDING',
  DateTime? reviewedAt,
  String? reason,
}) => Withdrawal(
  withdrawalId: id,
  amount: amount,
  status: status,
  requestedAt: DateTime.utc(2026, 11, 5, 2),
  reviewedAt: reviewedAt,
  rejectReason: reason,
);

void main() {
  group('page model', () {
    test('reads a page of the list endpoint', () {
      final page = WithdrawalPage.fromJson({
        'items': [
          {
            'withdrawalId': 7,
            'amount': 100000,
            'status': 'REJECTED',
            'requestedAt': '2026-11-05T02:00:00Z',
            'reviewedAt': '2026-11-05T03:00:00Z',
            'rejectReason': 'Thiếu chứng từ',
          },
        ],
        'page': 2,
        'pageSize': 20,
        'total': 21,
      });
      expect([page.page, page.pageSize, page.total], [2, 20, 21]);
      expect(page.items.single.status, 'REJECTED');
      expect(page.items.single.rejectReason, 'Thiếu chứng từ');
    });

    test('tolerates a missing list', () {
      final page = WithdrawalPage.fromJson({});
      expect(page.items, isEmpty);
      expect(page.total, 0);
    });
  });

  group('rows', () {
    test(
      'a pending request says it waits for the Admin and has no decision time',
      () {
        final row = withdrawalRow(request());
        expect(row.amount, '100.000 đ');
        expect(row.statusLabel, 'Chờ duyệt');
        expect(row.tone, 'warn');
        expect(row.sent, 'Gửi lúc 05/11/2026 09:00');
        expect(row.decided, 'Đang chờ Admin duyệt');
        expect(row.pending, isTrue);
        expect(row.reason, isNull);
      },
    );

    test('a paid request shows when it was approved', () {
      final row = withdrawalRow(
        request(status: 'PAID', reviewedAt: DateTime.utc(2026, 11, 5, 3)),
      );
      expect(
        [row.statusLabel, row.tone, row.pending],
        ['Đã duyệt', 'ok', false],
      );
      expect(row.decided, 'Được duyệt lúc 05/11/2026 10:00');
    });

    test('a rejected request shows the time and the trimmed reason', () {
      final row = withdrawalRow(
        request(
          status: 'REJECTED',
          reviewedAt: DateTime.utc(2026, 11, 5, 3),
          reason: '  Thiếu chứng từ  ',
        ),
      );
      expect([row.statusLabel, row.tone], ['Bị từ chối', 'bad']);
      expect(row.decided, 'Bị từ chối lúc 05/11/2026 10:00');
      expect(row.reason, 'Thiếu chứng từ');
    });

    test('a rejection without a reason shows no reason line', () {
      expect(
        withdrawalRow(request(status: 'REJECTED', reason: '   ')).reason,
        isNull,
      );
      expect(withdrawalRow(request(status: 'REJECTED')).reason, isNull);
    });

    test('a reason on a request that was not rejected is not shown', () {
      expect(
        withdrawalRow(request(status: 'PAID', reason: 'x')).reason,
        isNull,
      );
    });

    test('a cancelled request is neutral, with or without a time', () {
      final withTime = withdrawalRow(
        request(status: 'CANCELLED', reviewedAt: DateTime.utc(2026, 11, 5, 3)),
      );
      expect([withTime.statusLabel, withTime.tone], ['Đã huỷ', 'neutral']);
      expect(withTime.decided, 'Bạn đã huỷ lúc 05/11/2026 10:00');
      expect(
        withdrawalRow(request(status: 'CANCELLED')).decided,
        'Bạn đã huỷ yêu cầu',
      );
    });

    test('decided text falls back to the raw status for an unknown value', () {
      expect(decidedText(request(status: 'WEIRD')), 'WEIRD');
    });

    test('paid and rejected without a time still say what happened', () {
      expect(decidedText(request(status: 'PAID')), 'Đã được duyệt');
      expect(decidedText(request(status: 'REJECTED')), 'Bị từ chối');
    });
  });

  test('the count line is empty for no request and counts otherwise', () {
    expect(withdrawalCountText(0), '');
    expect(withdrawalCountText(3), '3 yêu cầu');
  });
}
