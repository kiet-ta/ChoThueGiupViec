import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/payouts/models/wallet_models.dart';
import 'package:mobile/features/payouts/screens/wallet_screen.dart';
import 'package:mobile/features/payouts/services/wallet_service.dart';

const _pending = PendingWithdrawal(
  withdrawalId: 7,
  amount: 100000,
  status: 'PENDING',
  requestedAt: null,
);

Wallet _wallet({
  int balance = 288000,
  int available = 188000,
  int held = 0,
  PendingWithdrawal? pending,
  DateTime? next,
}) => Wallet(
  balance: balance,
  available: available,
  held: held,
  pendingWithdrawal: pending,
  nextAvailableAt: next,
);

Withdrawal _withdrawal(String status, int amount) => Withdrawal(
  withdrawalId: 8,
  amount: amount,
  status: status,
  requestedAt: null,
  reviewedAt: null,
  rejectReason: null,
);

class FakeWalletService implements IWalletService {
  Wallet wallet;
  Object? loadError;
  Object? requestError;
  Object? cancelError;
  Completer<Withdrawal>? requestGate;
  final requested = <int>[];
  final cancelled = <int>[];
  int loads = 0;

  FakeWalletService(this.wallet);

  @override
  Future<Wallet> getWallet() {
    loads++;
    if (loadError != null) return Future.error(loadError!);
    return Future.value(wallet);
  }

  @override
  Future<Withdrawal> requestWithdrawal(int amount) {
    requested.add(amount);
    if (requestGate != null) return requestGate!.future;
    if (requestError != null) return Future.error(requestError!);
    wallet = _wallet(
      balance: wallet.balance,
      available: wallet.available - amount,
      held: amount,
      pending: PendingWithdrawal(
        withdrawalId: 8,
        amount: amount,
        status: 'PENDING',
        requestedAt: DateTime.utc(2026, 11, 5, 2),
      ),
    );
    return Future.value(_withdrawal('PENDING', amount));
  }

  @override
  Future<Withdrawal> cancelWithdrawal(int withdrawalId) {
    cancelled.add(withdrawalId);
    if (cancelError != null) return Future.error(cancelError!);
    final amount = wallet.pendingWithdrawal?.amount ?? 0;
    wallet = _wallet(
      balance: wallet.balance,
      available: wallet.available + amount,
    );
    return Future.value(_withdrawal('CANCELLED', amount));
  }
}

Future<void> pumpWallet(WidgetTester tester, FakeWalletService service) async {
  tester.view.physicalSize = const Size(390, 2400);
  tester.view.devicePixelRatio = 1.0;
  addTearDown(() {
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
  await tester.pumpWidget(
    MaterialApp(
      theme: NordicTheme.lightTheme,
      home: WalletScreen(service: service),
    ),
  );
  await tester.pumpAndSettle();
}

String _text(WidgetTester tester, String key) =>
    tester.widget<Text>(find.byKey(Key(key))).data ?? '';

bool _submitEnabled(WidgetTester tester) =>
    tester
        .widget<ElevatedButton>(
          find.descendant(
            of: find.byKey(const Key('wallet-submit')),
            matching: find.byType(ElevatedButton),
          ),
        )
        .onPressed !=
    null;

void main() {
  group('WalletScreen (LEDGER-16)', () {
    testWidgets(
      'shows the withdrawable amount as the big number with the balance and held amount',
      (tester) async {
        final service = FakeWalletService(
          _wallet(
            balance: 288000,
            available: 188000,
            held: 100000,
            pending: _pending,
          ),
        );
        await pumpWallet(tester, service);

        expect(_text(tester, 'wallet-available'), '188.000 đ');
        expect(_text(tester, 'wallet-balance'), '288.000 đ');
        expect(_text(tester, 'wallet-held'), '100.000 đ');
        expect(find.text('Ví của bạn'), findsOneWidget);
      },
    );

    testWidgets('shows when the next part becomes withdrawable', (
      tester,
    ) async {
      final service = FakeWalletService(
        _wallet(next: DateTime.utc(2026, 11, 12, 2, 30)),
      );
      await pumpWallet(tester, service);

      expect(
        _text(tester, 'wallet-next'),
        'Một phần tiền sẽ có thể rút từ 12/11/2026 09:30.',
      );
    });

    testWidgets(
      'always shows the single-request warning, with and without a pending request',
      (tester) async {
        await pumpWallet(tester, FakeWalletService(_wallet()));
        expect(find.byKey(const Key('wallet-warning')), findsOneWidget);
        expect(find.textContaining('1 yêu cầu rút'), findsOneWidget);
        expect(find.byKey(const Key('wallet-pending-card')), findsNothing);
        expect(_submitEnabled(tester), isTrue);

        await pumpWallet(
          tester,
          FakeWalletService(_wallet(held: 100000, pending: _pending)),
        );
        expect(find.byKey(const Key('wallet-warning')), findsOneWidget);
      },
    );

    testWidgets(
      'disables the submit button while a request is pending and shows the request card with cancel',
      (tester) async {
        final service = FakeWalletService(
          _wallet(held: 100000, pending: _pending),
        );
        await pumpWallet(tester, service);

        expect(_submitEnabled(tester), isFalse);
        expect(find.byKey(const Key('wallet-pending-card')), findsOneWidget);
        expect(_text(tester, 'wallet-pending-status'), 'Chờ duyệt');
        expect(_text(tester, 'wallet-pending-amount'), '100.000 đ');
        expect(find.byKey(const Key('wallet-locked')), findsOneWidget);
        expect(find.byKey(const Key('wallet-cancel')), findsOneWidget);
      },
    );

    testWidgets(
      'sends the typed amount and then shows the new pending request',
      (tester) async {
        final service = FakeWalletService(_wallet());
        await pumpWallet(tester, service);

        await tester.enterText(
          find.byKey(const Key('wallet-amount')),
          '100.000',
        );
        await tester.tap(find.byKey(const Key('wallet-submit')));
        await tester.pumpAndSettle();

        expect(service.requested, [100000]);
        expect(find.byKey(const Key('wallet-pending-card')), findsOneWidget);
        expect(_text(tester, 'wallet-pending-amount'), '100.000 đ');
        expect(
          _text(tester, 'wallet-notice'),
          contains('Đã gửi yêu cầu rút 100.000 đ'),
        );
        expect(_submitEnabled(tester), isFalse);
      },
    );

    testWidgets(
      'does not send a number above the withdrawable amount and says the maximum',
      (tester) async {
        final service = FakeWalletService(_wallet(available: 188000));
        await pumpWallet(tester, service);

        await tester.enterText(
          find.byKey(const Key('wallet-amount')),
          '500000',
        );
        await tester.tap(find.byKey(const Key('wallet-submit')));
        await tester.pumpAndSettle();

        expect(service.requested, isEmpty);
        expect(
          _text(tester, 'wallet-field-error'),
          'Bạn chỉ có thể rút tối đa 188.000 đ.',
        );
      },
    );

    testWidgets('does not send an empty or wrong amount', (tester) async {
      final service = FakeWalletService(_wallet());
      await pumpWallet(tester, service);

      await tester.tap(find.byKey(const Key('wallet-submit')));
      await tester.pumpAndSettle();
      expect(_text(tester, 'wallet-field-error'), contains('Ví dụ: 100.000'));

      await tester.enterText(find.byKey(const Key('wallet-amount')), 'abc');
      await tester.pump();
      expect(find.byKey(const Key('wallet-field-error')), findsNothing);
      await tester.tap(find.byKey(const Key('wallet-submit')));
      await tester.pumpAndSettle();

      expect(service.requested, isEmpty);
      expect(find.byKey(const Key('wallet-field-error')), findsOneWidget);
    });

    testWidgets('sends one request for two quick taps', (tester) async {
      final service = FakeWalletService(_wallet())
        ..requestGate = Completer<Withdrawal>();
      await pumpWallet(tester, service);

      await tester.enterText(find.byKey(const Key('wallet-amount')), '100000');
      await tester.tap(find.byKey(const Key('wallet-submit')));
      await tester.pump();
      await tester.tap(
        find.byKey(const Key('wallet-submit')),
        warnIfMissed: false,
      );
      await tester.pump();

      expect(service.requested, [100000]);
      service.requestGate!.complete(_withdrawal('PENDING', 100000));
      await tester.pumpAndSettle();
    });

    testWidgets(
      'translates a 409 for a request that already exists and reloads the wallet',
      (tester) async {
        final service = FakeWalletService(_wallet())
          ..requestError = const ApiException(
            409,
            'raw',
            code: 'WITHDRAWAL_PENDING',
          );
        await pumpWallet(tester, service);
        final loadsBefore = service.loads;

        await tester.enterText(
          find.byKey(const Key('wallet-amount')),
          '100000',
        );
        await tester.tap(find.byKey(const Key('wallet-submit')));
        await tester.pumpAndSettle();

        expect(
          _text(tester, 'wallet-error'),
          contains('đang có một yêu cầu chờ duyệt'),
        );
        expect(service.loads, loadsBefore + 1);
      },
    );

    testWidgets('cancels the pending request and unlocks the form', (
      tester,
    ) async {
      final service = FakeWalletService(
        _wallet(held: 100000, pending: _pending, available: 88000),
      );
      await pumpWallet(tester, service);

      await tester.tap(find.byKey(const Key('wallet-cancel')));
      await tester.pumpAndSettle();

      expect(service.cancelled, [7]);
      expect(find.byKey(const Key('wallet-pending-card')), findsNothing);
      expect(_submitEnabled(tester), isTrue);
      expect(
        _text(tester, 'wallet-notice'),
        contains('Đã huỷ yêu cầu rút 100.000 đ'),
      );
    });

    testWidgets(
      'explains that nothing can be withdrawn yet and locks the button',
      (tester) async {
        final service = FakeWalletService(
          _wallet(available: 0, balance: 50000),
        );
        await pumpWallet(tester, service);

        expect(find.byKey(const Key('wallet-nothing')), findsOneWidget);
        expect(_submitEnabled(tester), isFalse);
      },
    );

    testWidgets(
      'tells an agency worker why there is no wallet, without a retry button',
      (tester) async {
        final service = FakeWalletService(_wallet())
          ..loadError = const ApiException(403, 'Forbidden');
        await pumpWallet(tester, service);

        expect(find.textContaining('không có ví riêng'), findsOneWidget);
        expect(find.text('Thử lại'), findsNothing);
        expect(find.byKey(const Key('wallet-submit')), findsNothing);
      },
    );

    testWidgets(
      'shows a retry button for a network failure and loads again on tap',
      (tester) async {
        final service = FakeWalletService(_wallet())
          ..loadError = const ApiException(0, 'Không kết nối được máy chủ.');
        await pumpWallet(tester, service);
        expect(find.text('Thử lại'), findsOneWidget);

        service.loadError = null;
        await tester.tap(find.text('Thử lại'));
        await tester.pumpAndSettle();

        expect(_text(tester, 'wallet-available'), '188.000 đ');
      },
    );
  });
}
