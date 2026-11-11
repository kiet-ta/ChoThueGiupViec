import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/payouts/models/wallet_models.dart';
import 'package:mobile/features/payouts/screens/wallet_screen.dart';
import 'package:mobile/features/payouts/screens/withdrawal_history_screen.dart';
import 'package:mobile/features/payouts/services/wallet_service.dart';
import 'package:mobile/features/payouts/services/withdrawal_history_service.dart';

Withdrawal _request(
  int id,
  String status, {
  int amount = 100000,
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

WithdrawalPage _page(List<Withdrawal> items, {int total = -1, int page = 1}) =>
    WithdrawalPage(
      items: items,
      page: page,
      pageSize: 20,
      total: total < 0 ? items.length : total,
    );

class FakeWithdrawalHistory implements IWithdrawalHistoryService {
  final calls = <int>[];
  final pages = <WithdrawalPage>[];
  Object? error;
  Object? moreError;

  @override
  Future<WithdrawalPage> getWithdrawals({int page = 1, int pageSize = 20}) {
    calls.add(page);
    if (page > 1 && moreError != null) return Future.error(moreError!);
    if (error != null) return Future.error(error!);
    return Future.value(pages[page - 1]);
  }
}

Future<void> _pump(WidgetTester tester, FakeWithdrawalHistory service) async {
  tester.view.physicalSize = const Size(390, 2800);
  tester.view.devicePixelRatio = 1.0;
  addTearDown(() {
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
  await tester.pumpWidget(
    MaterialApp(
      theme: NordicTheme.lightTheme,
      home: WithdrawalHistoryScreen(service: service),
    ),
  );
  await tester.pumpAndSettle();
}

String _text(WidgetTester tester, String key) =>
    tester.widget<Text>(find.byKey(Key(key))).data ?? '';

void main() {
  group('WithdrawalHistoryScreen (LEDGER-19)', () {
    testWidgets(
      'lists each request with its amount, status, send time and what happened',
      (tester) async {
        final service = FakeWithdrawalHistory()
          ..pages.add(
            _page([
              _request(4, 'PENDING'),
              _request(
                3,
                'PAID',
                amount: 130000,
                reviewedAt: DateTime.utc(2026, 11, 5, 3),
              ),
              _request(
                2,
                'REJECTED',
                reviewedAt: DateTime.utc(2026, 11, 5, 3),
                reason: 'Thiếu chứng từ',
              ),
              _request(1, 'CANCELLED', amount: 50000),
            ]),
          );
        await _pump(tester, service);

        expect(find.text('Lịch sử rút tiền'), findsOneWidget);
        expect(_text(tester, 'withdrawals-count'), '4 yêu cầu');
        expect(_text(tester, 'withdrawal-amount-4'), '100.000 đ');
        expect(_text(tester, 'withdrawal-status-4'), 'Chờ duyệt');
        expect(_text(tester, 'withdrawal-decided-4'), 'Đang chờ Admin duyệt');
        expect(_text(tester, 'withdrawal-status-3'), 'Đã duyệt');
        expect(_text(tester, 'withdrawal-amount-3'), '130.000 đ');
        expect(
          _text(tester, 'withdrawal-decided-3'),
          'Được duyệt lúc 05/11/2026 10:00',
        );
        expect(_text(tester, 'withdrawal-status-2'), 'Bị từ chối');
        expect(_text(tester, 'withdrawal-reason-2'), 'Lý do: Thiếu chứng từ');
        expect(_text(tester, 'withdrawal-status-1'), 'Đã huỷ');
        expect(find.text('Gửi lúc 05/11/2026 09:00'), findsNWidgets(4));
        expect(find.byKey(const Key('withdrawal-reason-3')), findsNothing);
      },
    );

    testWidgets('keeps the order the server sent (newest first)', (
      tester,
    ) async {
      final service = FakeWithdrawalHistory()
        ..pages.add(
          _page([
            _request(9, 'PENDING'),
            _request(5, 'PAID'),
            _request(2, 'PAID'),
          ]),
        );
      await _pump(tester, service);

      final y9 = tester
          .getTopLeft(find.byKey(const Key('withdrawal-row-9')))
          .dy;
      final y5 = tester
          .getTopLeft(find.byKey(const Key('withdrawal-row-5')))
          .dy;
      final y2 = tester
          .getTopLeft(find.byKey(const Key('withdrawal-row-2')))
          .dy;
      expect(y9 < y5 && y5 < y2, isTrue);
    });

    testWidgets(
      'loads the next page and hides the button when everything is shown',
      (tester) async {
        final service = FakeWithdrawalHistory()
          ..pages.addAll([
            _page([_request(2, 'PAID')], total: 2),
            _page([_request(1, 'CANCELLED')], total: 2, page: 2),
          ]);
        await _pump(tester, service);
        expect(find.byKey(const Key('withdrawals-more')), findsOneWidget);

        await tester.tap(find.byKey(const Key('withdrawals-more')));
        await tester.pumpAndSettle();

        expect(service.calls, [1, 2]);
        expect(find.byKey(const Key('withdrawal-row-2')), findsOneWidget);
        expect(find.byKey(const Key('withdrawal-row-1')), findsOneWidget);
        expect(find.byKey(const Key('withdrawals-more')), findsNothing);
      },
    );

    testWidgets('keeps the rows and says so when the next page fails', (
      tester,
    ) async {
      final service = FakeWithdrawalHistory()
        ..pages.add(_page([_request(2, 'PAID')], total: 2))
        ..moreError = const ApiException(0, 'Không kết nối được máy chủ.');
      await _pump(tester, service);

      await tester.tap(find.byKey(const Key('withdrawals-more')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('withdrawal-row-2')), findsOneWidget);
      expect(
        _text(tester, 'withdrawals-more-error'),
        'Không kết nối được máy chủ.',
      );
    });

    testWidgets('says plainly when there is no request yet', (tester) async {
      final service = FakeWithdrawalHistory()..pages.add(_page([]));
      await _pump(tester, service);

      expect(find.byKey(const Key('withdrawals-empty')), findsOneWidget);
      expect(find.byKey(const Key('withdrawals-more')), findsNothing);
    });

    testWidgets('explains a 403 without a retry button', (tester) async {
      final service = FakeWithdrawalHistory()
        ..error = const ApiException(403, 'Forbidden');
      await _pump(tester, service);

      expect(find.textContaining('không có ví riêng'), findsOneWidget);
      expect(find.text('Thử lại'), findsNothing);
    });

    testWidgets('retries a network failure and then shows the requests', (
      tester,
    ) async {
      final service = FakeWithdrawalHistory()
        ..pages.add(_page([_request(2, 'PAID')]))
        ..error = const ApiException(0, 'Không kết nối được máy chủ.');
      await _pump(tester, service);
      expect(find.text('Thử lại'), findsOneWidget);

      service.error = null;
      await tester.tap(find.text('Thử lại'));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('withdrawal-row-2')), findsOneWidget);
    });
  });

  testWidgets(
    'the wallet screen opens the withdrawal history from its button',
    (tester) async {
      tester.view.physicalSize = const Size(390, 2800);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });
      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: WalletScreen(service: _WalletStub()),
        ),
      );
      await tester.pumpAndSettle();

      await tester.ensureVisible(find.byKey(const Key('wallet-withdrawals')));
      await tester.tap(find.byKey(const Key('wallet-withdrawals')));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 500));

      expect(find.byType(WithdrawalHistoryScreen), findsOneWidget);
    },
  );
}

class _WalletStub implements IWalletService {
  @override
  Future<Wallet> getWallet() => Future.value(
    const Wallet(
      balance: 288000,
      available: 188000,
      held: 0,
      pendingWithdrawal: null,
      nextAvailableAt: null,
    ),
  );

  @override
  Future<Withdrawal> requestWithdrawal(int amount) =>
      throw UnimplementedError();

  @override
  Future<Withdrawal> cancelWithdrawal(int withdrawalId) =>
      throw UnimplementedError();
}
