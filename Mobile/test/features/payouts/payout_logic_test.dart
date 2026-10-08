import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/payouts/logic/payout_logic.dart';
import 'package:mobile/features/payouts/models/payout_models.dart';

void main() {
  group('monthKeyOf reads the Ho Chi Minh calendar (G-3)', () {
    test('an instant late on the last UTC day already belongs to the next local month', () {
      // 2026-09-30 17:00 UTC is 2026-10-01 00:00 in Ho Chi Minh.
      expect(monthKeyOf(DateTime.utc(2026, 9, 30, 17)), '2026-10');
      expect(monthKeyOf(DateTime.utc(2026, 9, 30, 16, 59)), '2026-09');
    });

    test('the year changes at 17:00 UTC on 31 December', () {
      expect(monthKeyOf(DateTime.utc(2026, 12, 31, 17)), '2027-01');
    });
  });

  group('month navigation', () {
    test('shiftMonth crosses year boundaries both ways', () {
      expect(shiftMonth('2026-01', -1), '2025-12');
      expect(shiftMonth('2026-12', 1), '2027-01');
      expect(shiftMonth('2026-10', -10), '2025-12');
      expect(shiftMonth('2026-10', 0), '2026-10');
    });

    test('never goes past the current month, because the server refuses a future one', () {
      expect(canGoNext('2026-09', '2026-10'), isTrue);
      expect(canGoNext('2026-10', '2026-10'), isFalse);
      expect(canGoNext('2025-12', '2026-01'), isTrue);
    });

    test('stops at January of the first selectable year', () {
      expect(canGoPrevious('2024-02'), isTrue);
      expect(canGoPrevious('2024-01'), isFalse);
      expect(canGoPrevious('2026-01'), isTrue);
    });

    test('labels the month in Vietnamese without a leading zero', () {
      expect(monthLabel('2026-10'), 'Tháng 10/2026');
      expect(monthLabel('2026-03'), 'Tháng 3/2026');
    });
  });

  group('formatVnd', () {
    test('groups thousands with dots and ends with the dong sign', () {
      expect(formatVnd(0), '0 đ');
      expect(formatVnd(999), '999 đ');
      expect(formatVnd(1000), '1.000 đ');
      expect(formatVnd(1234567), '1.234.567 đ');
      expect(formatVnd(100000000), '100.000.000 đ');
    });

    test('keeps the sign of a negative figure', () {
      expect(formatVnd(-12500), '-12.500 đ');
    });
  });

  group('Ho Chi Minh date and time', () {
    test('adds seven hours and can change the day', () {
      expect(formatVietnamDateTime(DateTime.utc(2026, 9, 30, 17, 5)), '01/10/2026 00:05');
      expect(formatVietnamDateTime(DateTime.utc(2026, 10, 1, 0, 30)), '01/10/2026 07:30');
    });

    test('the completedAt of a job is read as a UTC instant and shown by its local date', () {
      final job = EarningsJob.fromJson({'completedAt': '2026-10-01T00:30:00Z'});

      expect(formatVietnamDate(job.completedAt), '01/10/2026');
    });

    test('shows a dash for a missing instant', () {
      expect(formatVietnamDateTime(null), '—');
      expect(formatVietnamDate(null), '—');
    });
  });

  group('payout status text', () {
    test('has a label and a hint for each status and falls back to not built', () {
      expect(payoutStatusLabel(PayoutStatus.notBuilt), 'Chưa chốt đợt');
      expect(payoutStatusLabel(PayoutStatus.pending), 'Chờ chuyển khoản');
      expect(payoutStatusLabel(PayoutStatus.transferred), 'Đã chuyển khoản');
      expect(payoutStatusLabel('SOMETHING'), 'Chưa chốt đợt');
      expect(payoutStatusHint(PayoutStatus.notBuilt), contains('theo tháng'));
      expect(payoutStatusHint(PayoutStatus.pending), contains('Admin'));
      expect(payoutStatusHint(PayoutStatus.transferred), contains('đã được chuyển'));
    });
  });

  group('earningsFailureMessage', () {
    test('403 says the agency is paid, not the worker', () {
      expect(earningsFailureMessage(const ApiException(403, 'x')), contains('đại lý'));
    });

    test('404, 400, a network failure and another status have their own text', () {
      expect(earningsFailureMessage(const ApiException(404, 'x')), contains('Không tìm thấy'));
      expect(earningsFailureMessage(const ApiException(400, 'x')), contains('không hợp lệ'));
      expect(earningsFailureMessage(const ApiException(0, 'Không kết nối được máy chủ.')), 'Không kết nối được máy chủ.');
      expect(earningsFailureMessage(const ApiException(500, 'Boom')), 'Boom');
    });
  });

  group('models read the contract shape', () {
    test('Earnings reads the totals, the status and every job', () {
      final e = Earnings.fromJson({
        'periodMonth': '2026-09',
        'jobCount': 2,
        'grossAmount': 600000,
        'commissionAmount': 120000,
        'penaltyAmount': 50000,
        'netAmount': 430000,
        'payoutStatus': 'PENDING',
        'jobs': [
          {'assignmentId': 1, 'orderId': 10, 'completedAt': '2026-09-02T03:00:00Z', 'grossAmount': 400000, 'commissionAmount': 80000, 'netAmount': 320000, 'absenceFee': false},
          {'assignmentId': 2, 'orderId': 11, 'completedAt': '2026-09-05T03:00:00Z', 'grossAmount': 200000, 'commissionAmount': 0, 'netAmount': 200000, 'absenceFee': true},
        ],
      });

      expect(e.periodMonth, '2026-09');
      expect(e.netAmount, 430000);
      expect(e.payoutStatus, PayoutStatus.pending);
      expect(e.jobs, hasLength(2));
      expect(e.jobs[1].absenceFee, isTrue);
      expect(e.jobs[0].completedAt, DateTime.utc(2026, 9, 2, 3));
    });

    test('Earnings tolerates missing fields', () {
      final e = Earnings.fromJson({});

      expect(e.jobs, isEmpty);
      expect(e.netAmount, 0);
      expect(e.payoutStatus, PayoutStatus.notBuilt);
    });

    test('PayoutHistoryPage reads items and the paging numbers', () {
      final page = PayoutHistoryPage.fromJson({
        'items': [
          {'batchId': 5, 'periodMonth': '2026-08', 'netAmount': 900000, 'itemStatus': 'TRANSFERRED', 'transferredAt': '2026-09-03T02:00:00Z'},
        ],
        'page': 1,
        'pageSize': 20,
        'total': 1,
      });

      expect(page.items.single.batchId, 5);
      expect(page.items.single.transferredAt, DateTime.utc(2026, 9, 3, 2));
      expect(page.total, 1);
    });
  });
}
