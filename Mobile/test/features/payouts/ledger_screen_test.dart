import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/payouts/models/ledger_models.dart';
import 'package:mobile/features/payouts/screens/ledger_history_screen.dart';
import 'package:mobile/features/payouts/screens/ledger_statement_screen.dart';
import 'package:mobile/features/payouts/services/ledger_service.dart';

LedgerEntry _entry(
  int id, {
  String type = 'JOB_EARNING',
  int amount = 208000,
  int? signed = 208000,
  String fromType = 'PLATFORM_CLEARING',
  String toType = 'WORKER',
}) => LedgerEntry(
  entryId: id,
  entryCode: 'LG${id.toString().padLeft(8, '0')}',
  createdAt: DateTime.utc(2026, 11, 5, 3, 30),
  entryType: type,
  fromPartyType: fromType,
  fromPartyId: null,
  toPartyType: toType,
  toPartyId: 3,
  amount: amount,
  refType: 'ASSIGNMENT',
  refId: id,
  orderId: 41,
  entryHash: 'abcdef0123456789',
  signedAmount: signed,
);

LedgerHistoryPage _page(
  List<LedgerEntry> items, {
  int total = -1,
  int page = 1,
}) => LedgerHistoryPage(
  items: items,
  openingBalance: 100000,
  closingBalance: 308000,
  page: page,
  pageSize: 20,
  total: total < 0 ? items.length : total,
);

class _Call {
  final String? from;
  final String? to;
  final String? type;
  final int page;

  _Call(this.from, this.to, this.type, this.page);
}

class FakeLedgerService implements ILedgerService {
  final calls = <_Call>[];
  final statements = <String>[];
  final pages = <LedgerHistoryPage>[];
  Object? error;
  Object? moreError;
  WorkerStatement? statement;
  Object? statementError;
  Completer<LedgerHistoryPage>? gate;

  @override
  Future<LedgerHistoryPage> getLedger({
    String? from,
    String? to,
    String? type,
    int page = 1,
    int pageSize = 20,
  }) {
    calls.add(_Call(from, to, type, page));
    if (gate != null) return gate!.future;
    if (page > 1 && moreError != null) return Future.error(moreError!);
    if (error != null) return Future.error(error!);
    return Future.value(pages[page - 1]);
  }

  @override
  Future<WorkerStatement> getStatement(String month) {
    statements.add(month);
    if (statementError != null) return Future.error(statementError!);
    return Future.value(statement!);
  }
}

void _tallView(WidgetTester tester) {
  tester.view.physicalSize = const Size(390, 2800);
  tester.view.devicePixelRatio = 1.0;
  addTearDown(() {
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
}

Future<void> _pumpHistory(
  WidgetTester tester,
  FakeLedgerService service,
) async {
  _tallView(tester);
  await tester.pumpWidget(
    MaterialApp(
      theme: NordicTheme.lightTheme,
      home: LedgerHistoryScreen(
        service: service,
        now: () => DateTime.utc(2026, 11, 15, 3),
      ),
    ),
  );
  await tester.pumpAndSettle();
}

Future<void> _pumpStatement(
  WidgetTester tester,
  FakeLedgerService service,
) async {
  _tallView(tester);
  await tester.pumpWidget(
    MaterialApp(
      theme: NordicTheme.lightTheme,
      home: LedgerStatementScreen(month: '2026-10', service: service),
    ),
  );
  await tester.pumpAndSettle();
}

String _text(WidgetTester tester, String key) =>
    tester.widget<Text>(find.byKey(Key(key))).data ?? '';

void main() {
  group('LedgerHistoryScreen (LEDGER-17)', () {
    testWidgets(
      'opens on the current month and shows the balances and the lines with code and signed amount',
      (tester) async {
        final service = FakeLedgerService()
          ..pages.add(
            _page([
              _entry(5),
              _entry(
                6,
                type: 'WITHDRAWAL',
                fromType: 'WORKER',
                toType: 'EXTERNAL',
                amount: 100000,
                signed: -100000,
              ),
            ]),
          );
        await _pumpHistory(tester, service);

        expect(_text(tester, 'ledger-month'), 'Tháng 11/2026');
        expect(service.calls.single.from, '2026-11-01');
        expect(service.calls.single.to, '2026-11-30');
        expect(service.calls.single.type, isNull);
        expect(_text(tester, 'ledger-opening'), '100.000 đ');
        expect(_text(tester, 'ledger-closing'), '308.000 đ');
        expect(_text(tester, 'ledger-change'), '+208.000 đ');
        expect(_text(tester, 'ledger-amount-5'), '+208.000 đ');
        expect(_text(tester, 'ledger-amount-6'), '-100.000 đ');
        expect(find.textContaining('LG00000005'), findsOneWidget);
        expect(find.text('Tiền công ca'), findsOneWidget);
      },
    );

    testWidgets('filters by type and sends the type to the server', (
      tester,
    ) async {
      final service = FakeLedgerService()..pages.add(_page([_entry(5)]));
      await _pumpHistory(tester, service);

      await tester.tap(find.byKey(const Key('ledger-filter-WITHDRAWAL')));
      await tester.pumpAndSettle();

      expect(service.calls.last.type, 'WITHDRAWAL');
      expect(service.calls.length, 2);

      await tester.tap(find.byKey(const Key('ledger-filter-all')));
      await tester.pumpAndSettle();
      expect(service.calls.last.type, isNull);
    });

    testWidgets('goes back a month and cannot go past the current one', (
      tester,
    ) async {
      final service = FakeLedgerService()..pages.add(_page([_entry(5)]));
      await _pumpHistory(tester, service);

      expect(
        tester
            .widget<IconButton>(find.byKey(const Key('ledger-next')))
            .onPressed,
        isNull,
      );
      await tester.tap(find.byKey(const Key('ledger-prev')));
      await tester.pumpAndSettle();

      expect(_text(tester, 'ledger-month'), 'Tháng 10/2026');
      expect(service.calls.last.from, '2026-10-01');
      expect(service.calls.last.to, '2026-10-31');
    });

    testWidgets(
      'loads the next page and hides the button when everything is shown',
      (tester) async {
        final service = FakeLedgerService()
          ..pages.addAll([
            _page([_entry(5)], total: 2),
            _page([_entry(6)], total: 2, page: 2),
          ]);
        await _pumpHistory(tester, service);
        expect(find.byKey(const Key('ledger-more')), findsOneWidget);

        await tester.tap(find.byKey(const Key('ledger-more')));
        await tester.pumpAndSettle();

        expect(service.calls.last.page, 2);
        expect(find.byKey(const Key('ledger-row-5')), findsOneWidget);
        expect(find.byKey(const Key('ledger-row-6')), findsOneWidget);
        expect(find.byKey(const Key('ledger-more')), findsNothing);
      },
    );

    testWidgets('keeps the lines and says so when the next page fails', (
      tester,
    ) async {
      final service = FakeLedgerService()
        ..pages.add(_page([_entry(5)], total: 2))
        ..moreError = const ApiException(0, 'Không kết nối được máy chủ.');
      await _pumpHistory(tester, service);

      await tester.tap(find.byKey(const Key('ledger-more')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('ledger-row-5')), findsOneWidget);
      expect(_text(tester, 'ledger-more-error'), 'Không kết nối được máy chủ.');
    });

    testWidgets('shows an empty message for a month without lines', (
      tester,
    ) async {
      final service = FakeLedgerService()..pages.add(_page([]));
      await _pumpHistory(tester, service);

      expect(find.byKey(const Key('ledger-empty')), findsOneWidget);
      expect(find.byKey(const Key('ledger-more')), findsNothing);
    });

    testWidgets('explains a 403 without a retry button', (tester) async {
      final forbidden = FakeLedgerService()
        ..error = const ApiException(403, 'Forbidden');
      await _pumpHistory(tester, forbidden);

      expect(find.textContaining('không có ví riêng'), findsOneWidget);
      expect(find.text('Thử lại'), findsNothing);
    });

    testWidgets('retries a network failure and then shows the lines', (
      tester,
    ) async {
      final flaky = FakeLedgerService()
        ..pages.add(_page([_entry(5)]))
        ..error = const ApiException(0, 'Không kết nối được máy chủ.');
      await _pumpHistory(tester, flaky);
      expect(find.text('Thử lại'), findsOneWidget);

      flaky.error = null;
      await tester.tap(find.text('Thử lại'));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('ledger-row-5')), findsOneWidget);
    });

    testWidgets('opens the statement of the month on the button', (
      tester,
    ) async {
      final service = FakeLedgerService()
        ..pages.add(_page([_entry(5)]))
        ..statement = const WorkerStatement(
          periodMonth: '2026-11',
          openingBalance: 100000,
          items: [],
          advanceAmount: 0,
          penaltyAmount: 0,
          settlementAmount: 0,
          closingBalance: 308000,
          lastHash: 'ff00',
        );
      await _pumpHistory(tester, service);

      await tester.ensureVisible(find.byKey(const Key('ledger-statement')));
      await tester.tap(find.byKey(const Key('ledger-statement')));
      await tester.pumpAndSettle();

      expect(service.statements, ['2026-11']);
      expect(find.byKey(const Key('statement-closing')), findsOneWidget);
    });
  });

  group('LedgerStatementScreen (LEDGER-17)', () {
    WorkerStatement statement({List<LedgerEntry>? items}) => WorkerStatement(
      periodMonth: '2026-10',
      openingBalance: 20000,
      items:
          items ??
          [
            _entry(5),
            _entry(
              6,
              type: 'WITHDRAWAL',
              fromType: 'WORKER',
              toType: 'EXTERNAL',
              amount: 100000,
              signed: -100000,
            ),
            _entry(
              7,
              type: 'SETTLEMENT',
              fromType: 'WORKER',
              toType: 'EXTERNAL',
              amount: 128000,
              signed: -128000,
            ),
          ],
      advanceAmount: 100000,
      penaltyAmount: 0,
      settlementAmount: 128000,
      closingBalance: 0,
      lastHash: 'ff00ff00',
    );

    testWidgets(
      'shows opening, advance, penalty, settlement, closing and the last hash',
      (tester) async {
        final service = FakeLedgerService()..statement = statement();
        await _pumpStatement(tester, service);

        expect(service.statements, ['2026-10']);
        expect(find.text('Sao kê tháng 10/2026'), findsOneWidget);
        expect(_text(tester, 'statement-opening'), '20.000 đ');
        expect(_text(tester, 'statement-advance'), '-100.000 đ');
        expect(_text(tester, 'statement-penalty'), '—');
        expect(_text(tester, 'statement-settlement'), '-128.000 đ');
        expect(_text(tester, 'statement-closing'), '0 đ');
        expect(
          tester
              .widget<SelectableText>(find.byKey(const Key('statement-hash')))
              .data,
          'ff00ff00',
        );
      },
    );

    testWidgets('lists every entry of the month with the settlement last', (
      tester,
    ) async {
      final service = FakeLedgerService()..statement = statement();
      await _pumpStatement(tester, service);

      expect(find.byKey(const Key('statement-row-5')), findsOneWidget);
      expect(find.byKey(const Key('statement-row-6')), findsOneWidget);
      expect(find.byKey(const Key('statement-row-7')), findsOneWidget);
      expect(find.text('Tất toán'), findsOneWidget);
    });

    testWidgets('shows an empty message for a month without entries', (
      tester,
    ) async {
      final service = FakeLedgerService()..statement = statement(items: []);
      await _pumpStatement(tester, service);

      expect(find.byKey(const Key('statement-empty')), findsOneWidget);
    });

    testWidgets('explains a failure and retries', (tester) async {
      final service = FakeLedgerService()
        ..statement = statement()
        ..statementError = const ApiException(0, 'Không kết nối được máy chủ.');
      await _pumpStatement(tester, service);
      expect(find.text('Thử lại'), findsOneWidget);

      service.statementError = null;
      await tester.tap(find.text('Thử lại'));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('statement-closing')), findsOneWidget);
    });
  });
}
