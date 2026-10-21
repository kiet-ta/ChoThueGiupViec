import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/workers/models/customer_acceptance_models.dart';

/// MOB-ACCEPT-01 (#428): the server's amounts are decimals; the acceptance answer must read `260000` and `260000.0` alike.
void main() {
  Map<String, dynamic> json({
    Object? gross = 260000,
    Object? payout = 208000,
    Object? rate = 0.2,
  }) => {
    'assignmentId': 5,
    'status': 'COMPLETED',
    'grossAmount': gross,
    'commissionRate': rate,
    'payoutAmount': payout,
    'completedAt': '2026-10-21T10:48:00Z',
  };

  test('whole numbers as int', () {
    final dto = AssignmentCompletionDto.fromJson(json());

    expect(dto.grossAmount, 260000);
    expect(dto.payoutAmount, 208000);
    expect(dto.commissionRate, 0.2);
  });

  test('whole numbers as double, the way a decimal arrives', () {
    final dto = AssignmentCompletionDto.fromJson(
      json(gross: 260000.0, payout: 208000.0, rate: 0.2),
    );

    expect(dto.grossAmount, 260000);
    expect(dto.payoutAmount, 208000);
  });

  test('a fractional amount is rounded to whole VND', () {
    final dto = AssignmentCompletionDto.fromJson(
      json(gross: 260000.4, payout: 207999.5),
    );

    expect(dto.grossAmount, 260000);
    expect(dto.payoutAmount, 208000);
  });

  test('a missing or non-numeric amount is 0', () {
    final dto = AssignmentCompletionDto.fromJson({
      'assignmentId': 5,
      'payoutAmount': 'x',
    });

    expect(dto.grossAmount, 0);
    expect(dto.payoutAmount, 0);
  });

  test('the commission rate reads an int 0 (a Pro order) and a double', () {
    expect(AssignmentCompletionDto.fromJson(json(rate: 0)).commissionRate, 0.0);
    expect(
      AssignmentCompletionDto.fromJson(json(rate: 0.0)).commissionRate,
      0.0,
    );
    expect(
      AssignmentCompletionDto.fromJson(json(rate: 0.2)).commissionRate,
      0.2,
    );
  });
}
