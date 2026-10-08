import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/dispatch/models/customer_absent_report.dart';

void main() {
  group('CustomerAbsentReport Model Tests', () {
    test('CustomerAbsentReport fromJson accurately parses BR-05 40% compensation fields', () {
      final json = {
        'assignmentId': 1001,
        'reportedAt': '2026-10-15T08:16:00Z',
        'checkedInAt': '2026-10-15T07:55:00Z',
        'elapsedMinutes': 21,
        'callAttempts': 3,
        'reviewStatus': 'PENDING_APPROVAL',
        'workerFeeRate': 0.40,
        'absenceFeeAmount': 104000.0,
        'customerRefundRate': 0.60,
        'customerRefundAmount': 156000.0,
      };

      final report = CustomerAbsentReport.fromJson(json);

      expect(report.assignmentId, 1001);
      expect(report.elapsedMinutes, 21);
      expect(report.callAttempts, 3);
      expect(report.reviewStatus, 'PENDING_APPROVAL');
      expect(report.workerFeeRate, 0.40);
      expect(report.absenceFeeAmount, 104000.0);
      expect(report.customerRefundRate, 0.60);
      expect(report.customerRefundAmount, 156000.0);
    });

    test('CallLogResult fromJson parses call attempt count', () {
      final json = {
        'assignmentId': 1001,
        'callAttempts': 2,
        'loggedAt': '2026-10-15T08:05:00Z',
      };

      final log = CallLogResult.fromJson(json);

      expect(log.assignmentId, 1001);
      expect(log.callAttempts, 2);
    });

    test('CustomerAbsentReport roundtrip toJson maintains data integrity', () {
      final report = CustomerAbsentReport(
        assignmentId: 1005,
        reportedAt: DateTime.utc(2026, 10, 15, 8, 30),
        checkedInAt: DateTime.utc(2026, 10, 15, 8, 0),
        elapsedMinutes: 30,
        callAttempts: 2,
        reviewStatus: 'APPROVED',
        workerFeeRate: 0.40,
        absenceFeeAmount: 120000.0,
        customerRefundRate: 0.60,
        customerRefundAmount: 180000.0,
      );

      final json = report.toJson();
      final roundtrip = CustomerAbsentReport.fromJson(json);

      expect(roundtrip.assignmentId, 1005);
      expect(roundtrip.absenceFeeAmount, 120000.0);
      expect(roundtrip.workerFeeRate, 0.40);
    });
  });
}
