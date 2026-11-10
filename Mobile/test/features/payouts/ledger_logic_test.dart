import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/payouts/logic/ledger_logic.dart';
import 'package:mobile/features/payouts/models/ledger_models.dart';

LedgerEntry entry({
  int id = 5,
  String type = 'JOB_EARNING',
  String fromType = 'PLATFORM_CLEARING',
  int? fromId,
  String toType = 'WORKER',
  int? toId = 3,
  int amount = 208000,
  String refType = 'ASSIGNMENT',
  int refId = 10,
  int? orderId = 41,
  int? signed = 208000,
}) => LedgerEntry(
  entryId: id,
  entryCode: 'LG${id.toString().padLeft(8, '0')}',
  createdAt: DateTime.utc(2026, 11, 5, 3, 30),
  entryType: type,
  fromPartyType: fromType,
  fromPartyId: fromId,
  toPartyType: toType,
  toPartyId: toId,
  amount: amount,
  refType: refType,
  refId: refId,
  orderId: orderId,
  entryHash: 'abcdef0123456789abcdef0123456789',
  signedAmount: signed,
);

void main() {
  group('models', () {
    test('read a history page with the balances', () {
      final page = LedgerHistoryPage.fromJson({
        'items': [
          {
            'entryId': 5,
            'entryCode': 'LG00000005',
            'createdAt': '2026-11-05T03:30:00Z',
            'entryType': 'JOB_EARNING',
            'fromPartyType': 'PLATFORM_CLEARING',
            'fromPartyId': null,
            'toPartyType': 'WORKER',
            'toPartyId': 3,
            'amount': 208000,
            'refType': 'ASSIGNMENT',
            'refId': 10,
            'orderId': 41,
            'note': null,
            'entryHash': 'abc',
            'account': 'WORKER',
            'signedAmount': 208000,
          },
        ],
        'openingBalance': 100000,
        'closingBalance': 308000,
        'page': 1,
        'pageSize': 20,
        'total': 1,
      });

      expect(page.items.single.signedAmount, 208000);
      expect(page.items.single.createdAt, DateTime.utc(2026, 11, 5, 3, 30));
      expect(
        [page.openingBalance, page.closingBalance, page.total],
        [100000, 308000, 1],
      );
    });

    test('read a statement and tolerate missing fields', () {
      final s = WorkerStatement.fromJson({
        'periodMonth': '2026-10',
        'openingBalance': 0,
        'items': [],
        'advanceAmount': 100000,
        'penaltyAmount': 50000,
        'settlementAmount': 300000,
        'closingBalance': 0,
        'lastHash': 'ff00',
      });
      expect(
        [s.periodMonth, s.advanceAmount, s.penaltyAmount, s.settlementAmount],
        ['2026-10', 100000, 50000, 300000],
      );
      expect(s.lastHash, 'ff00');

      final empty = WorkerStatement.fromJson({});
      expect(empty.items, isEmpty);
      expect(empty.lastHash, isNull);
    });
  });

  group('labels', () {
    test('name types, parties and references in plain words', () {
      expect(entryTypeLabel('JOB_EARNING'), 'Tiền công ca');
      expect(entryTypeLabel('WITHDRAWAL'), 'Rút tiền (ứng lương)');
      expect(entryTypeLabel('UNKNOWN'), 'UNKNOWN');
      expect(partyLabel('PLATFORM_CLEARING', null), 'Tiền giữ hộ khách');
      expect(partyLabel('WORKER', 3), 'Ví của bạn');
      expect(partyLabel('AGENCY', 4), 'Agency #4');
      expect(referenceLabel('ASSIGNMENT', 10, 41), 'Ca #10 · đơn #41');
      expect(referenceLabel('ORDER', 41, 41), 'Đơn #41');
      expect(referenceLabel('WITHDRAWAL', 7, null), 'Yêu cầu rút #7');
    });

    test('sign amounts and shorten hashes', () {
      expect(signedAmountText(208000, 208000), '+208.000 đ');
      expect(signedAmountText(-100000, 100000), '-100.000 đ');
      expect(signedAmountText(null, 5000), '5.000 đ');
      expect(shortHash('abcdef0123456789'), 'abcdef01…');
      expect(shortHash('abc'), 'abc');
      expect(shortHash(null), '—');
    });
  });

  group('rows', () {
    test('map an incoming earning with the other side, the code and the local time', () {
      final row = ledgerRow(entry());
      expect(
        [
          row.code,
          row.time,
          row.type,
          row.counterparty,
          row.amount,
          row.tone,
          row.reference,
          row.settlement,
        ],
        [
          'LG00000005',
          '05/11/2026 10:30',
          'Tiền công ca',
          'Tiền giữ hộ khách',
          '+208.000 đ',
          'in',
          'Ca #10 · đơn #41',
          false,
        ],
      );
    });

    test(
      'map a withdrawal going out to the outside and highlight the settlement',
      () {
        final out = ledgerRow(
          entry(
            type: 'WITHDRAWAL',
            fromType: 'WORKER',
            fromId: 3,
            toType: 'EXTERNAL',
            toId: null,
            refType: 'WITHDRAWAL',
            refId: 7,
            orderId: null,
            amount: 100000,
            signed: -100000,
          ),
        );
        expect(
          [out.type, out.counterparty, out.amount, out.tone, out.reference],
          [
            'Rút tiền (ứng lương)',
            'Bên ngoài',
            '-100.000 đ',
            'out',
            'Yêu cầu rút #7',
          ],
        );

        final settle = ledgerRow(entry(type: 'SETTLEMENT', signed: -300000));
        expect(settle.settlement, isTrue);
        expect(settle.type, 'Tất toán lương tháng');
      },
    );

    test('show both sides when the server sent no sign', () {
      final row = ledgerRow(entry(signed: null));
      expect(
        [row.tone, row.amount, row.counterparty],
        ['plain', '208.000 đ', 'Tiền giữ hộ khách → Ví của bạn'],
      );
    });
  });

  group('month and balances', () {
    test('bound a month by its first and last Vietnam day', () {
      expect(monthDayRange('2026-11'), (from: '2026-11-01', to: '2026-11-30'));
      expect(monthDayRange('2026-12'), (from: '2026-12-01', to: '2026-12-31'));
      expect(monthDayRange('2024-02'), (from: '2024-02-01', to: '2024-02-29'));
      expect(monthDayRange('2026-02'), (from: '2026-02-01', to: '2026-02-28'));
    });

    test('show the change of the balance with its sign', () {
      expect(balanceChangeText(100000, 308000), '+208.000 đ');
      expect(balanceChangeText(308000, 100000), '-208.000 đ');
      expect(balanceChangeText(5000, 5000), '0 đ');
    });

    test('offer the filters a worker needs, all first', () {
      expect(ledgerTypeFilters.first.value, isNull);
      expect(ledgerTypeFilters.map((f) => f.value), [
        null,
        'JOB_EARNING',
        'WITHDRAWAL',
        'PENALTY',
        'SETTLEMENT',
      ]);
    });
  });

  group('statement summary', () {
    test('lists opening, advance, penalty, settlement and closing, with dashes for zero', () {
      final s = WorkerStatement(
        periodMonth: '2026-10',
        openingBalance: 20000,
        items: const [],
        advanceAmount: 100000,
        penaltyAmount: 0,
        settlementAmount: 300000,
        closingBalance: 0,
        lastHash: 'ff',
      );
      expect(statementSummary(s).map((l) => [l.key, l.value]).toList(), [
        ['opening', '20.000 đ'],
        ['advance', '-100.000 đ'],
        ['penalty', '—'],
        ['settlement', '-300.000 đ'],
        ['closing', '0 đ'],
      ]);
    });
  });

  group('errors', () {
    test('explain a forbidden, a bad period and a missing page', () {
      expect(
        ledgerFailureMessage(const ApiException(403, 'x')),
        contains('không có ví riêng'),
      );
      expect(
        ledgerFailureMessage(const ApiException(400, 'x')),
        contains('không hợp lệ'),
      );
      expect(
        ledgerFailureMessage(const ApiException(404, 'x')),
        contains('Không tìm thấy'),
      );
      expect(ledgerFailureMessage(const ApiException(500, 'boom')), 'boom');
    });
  });
}
