import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/payouts/models/wallet_models.dart';
import 'package:mobile/features/payouts/screens/bank_account_screen.dart';
import 'package:mobile/features/payouts/screens/wallet_screen.dart';
import 'package:mobile/features/payouts/services/wallet_service.dart';
import 'package:mobile/features/payouts/services/worker_bank_service.dart';

const _none = WorkerBank(bankName: '', bankAccountNo: '', isSet: false);
const _acb = WorkerBank(
  bankName: 'ACB',
  bankAccountNo: '0011001234567',
  isSet: true,
  canRequestChange: true,
);

final _pending = WorkerBank(
  bankName: 'ACB',
  bankAccountNo: '0011001234567',
  isSet: true,
  pendingRequest: BankChangePending(
    requestId: 4,
    bankName: 'Techcombank',
    bankAccountNo: '8800770066',
    requestedAt: DateTime.utc(2026, 11, 12, 18, 10),
  ),
  changeLockedUntil: DateTime.utc(2026, 11, 27, 18, 10),
  lastRequest: BankChangeLast(
    status: 'PENDING',
    requestedAt: DateTime.utc(2026, 11, 12, 18, 10),
  ),
);

WorkerBank _locked(BankChangeLast last) => WorkerBank(
  bankName: 'Techcombank',
  bankAccountNo: '8800770066',
  isSet: true,
  changeLockedUntil: DateTime.utc(2026, 11, 27, 18, 10),
  lastRequest: last,
);

class FakeWorkerBank implements IWorkerBankService {
  WorkerBank bank;
  Object? loadError;
  Object? saveError;
  Object? requestError;
  final saved = <({String name, String account})>[];
  final requests = <({String name, String account})>[];
  int loads = 0;

  FakeWorkerBank(this.bank);

  @override
  Future<WorkerBank> getBank() {
    loads++;
    if (loadError != null) return Future.error(loadError!);
    return Future.value(bank);
  }

  @override
  Future<WorkerBank> saveBank({
    required String bankName,
    required String bankAccountNo,
  }) {
    saved.add((name: bankName, account: bankAccountNo));
    if (saveError != null) return Future.error(saveError!);
    bank = WorkerBank(
      bankName: bankName,
      bankAccountNo: bankAccountNo,
      isSet: true,
      canRequestChange: true,
    );
    return Future.value(bank);
  }

  @override
  Future<WorkerBank> requestChange({
    required String bankName,
    required String bankAccountNo,
  }) {
    requests.add((name: bankName, account: bankAccountNo));
    if (requestError != null) return Future.error(requestError!);
    // like the server: the account in use stays, the request is pending and the lock starts
    bank = WorkerBank(
      bankName: bank.bankName,
      bankAccountNo: bank.bankAccountNo,
      isSet: true,
      pendingRequest: BankChangePending(
        requestId: 9,
        bankName: bankName,
        bankAccountNo: bankAccountNo,
        requestedAt: DateTime.utc(2026, 11, 12, 18, 10),
      ),
      changeLockedUntil: DateTime.utc(2026, 11, 27, 18, 10),
      lastRequest: BankChangeLast(
        status: 'PENDING',
        requestedAt: DateTime.utc(2026, 11, 12, 18, 10),
      ),
    );
    return Future.value(bank);
  }
}

Future<void> _pump(WidgetTester tester, FakeWorkerBank service) async {
  tester.view.physicalSize = const Size(390, 2800);
  tester.view.devicePixelRatio = 1.0;
  addTearDown(() {
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
  await tester.pumpWidget(
    MaterialApp(
      theme: NordicTheme.lightTheme,
      home: BankAccountScreen(service: service),
    ),
  );
  await tester.pumpAndSettle();
}

String _text(WidgetTester tester, String key) =>
    tester.widget<Text>(find.byKey(Key(key))).data ?? '';

/// The text of a field: the key is on the `NordicTextInput`, the controller on its `TextFormField`.
String _value(WidgetTester tester, String key) => tester
    .widget<TextFormField>(
      find.descendant(
        of: find.byKey(Key(key)),
        matching: find.byType(TextFormField),
      ),
    )
    .controller!
    .text;

Future<void> _fill(WidgetTester tester, {String? name, String? account}) async {
  if (name != null) {
    await tester.enterText(find.byKey(const Key('bank-name')), name);
  }
  if (account != null) {
    await tester.enterText(find.byKey(const Key('bank-account')), account);
  }
}

Future<void> _save(WidgetTester tester) async {
  await tester.tap(find.byKey(const Key('bank-save')));
  await tester.pumpAndSettle();
}

void main() {
  group('BankAccountScreen (LEDGER-20)', () {
    testWidgets(
      'says there is no account yet and offers to add one, with the sandbox note',
      (tester) async {
        await _pump(tester, FakeWorkerBank(_none));

        expect(find.text('Tài khoản nhận tiền'), findsOneWidget);
        expect(
          _text(tester, 'bank-summary-title'),
          'Chưa có tài khoản nhận tiền',
        );
        expect(_text(tester, 'bank-summary-status'), 'Chưa có');
        expect(find.textContaining('tài khoản ảo'), findsOneWidget);
        expect(find.text('Lưu tài khoản'), findsOneWidget);
      },
    );

    testWidgets(
      'shows a saved account with its number masked and a button to send a change request, not the form',
      (tester) async {
        await _pump(tester, FakeWorkerBank(_acb));

        expect(_text(tester, 'bank-summary-title'), 'ACB');
        expect(_text(tester, 'bank-summary-account'), '•••• 4567');
        expect(_text(tester, 'bank-summary-status'), 'Đã thiết lập');
        expect(find.byKey(const Key('bank-open-change')), findsOneWidget);
        expect(find.byKey(const Key('bank-name')), findsNothing);
        expect(find.byKey(const Key('bank-save')), findsNothing);
      },
    );

    testWidgets('a quick pick fills the bank name', (tester) async {
      await _pump(tester, FakeWorkerBank(_none));

      await tester.tap(find.byKey(const Key('bank-pick-Techcombank')));
      await tester.pump();

      expect(_value(tester, 'bank-name'), 'Techcombank');
    });

    testWidgets(
      'saves the trimmed name and the account without spaces, then shows it',
      (tester) async {
        final service = FakeWorkerBank(_none);
        await _pump(tester, service);

        await _fill(
          tester,
          name: '  Vietcombank ',
          account: '0011 0012 3456 7',
        );
        await _save(tester);

        expect(service.saved, [
          (name: 'Vietcombank', account: '00110012345 67'.replaceAll(' ', '')),
        ]);
        expect(_text(tester, 'bank-notice'), 'Đã lưu tài khoản nhận tiền.');
        expect(_text(tester, 'bank-summary-title'), 'Vietcombank');
        expect(_text(tester, 'bank-summary-account'), '•••• 4567');
        // the first account is saved: from now on a change is a request
        expect(find.byKey(const Key('bank-open-change')), findsOneWidget);
        expect(find.byKey(const Key('bank-save')), findsNothing);
      },
    );

    testWidgets(
      'does not send an empty form and says what is missing under each field',
      (tester) async {
        final service = FakeWorkerBank(_none);
        await _pump(tester, service);

        await _save(tester);

        expect(service.saved, isEmpty);
        expect(
          _text(tester, 'bank-name-error'),
          contains('Hãy nhập tên ngân hàng'),
        );
        expect(
          _text(tester, 'bank-account-error'),
          contains('Hãy nhập số tài khoản'),
        );
      },
    );

    testWidgets('does not send a short or non-digit account number', (
      tester,
    ) async {
      final service = FakeWorkerBank(_none);
      await _pump(tester, service);

      await _fill(tester, name: 'ACB', account: '12345');
      await _save(tester);
      expect(
        _text(tester, 'bank-account-error'),
        'Số tài khoản gồm 6 đến 30 chữ số.',
      );

      await _fill(tester, account: '12345a7');
      await _save(tester);
      expect(service.saved, isEmpty);
      expect(find.byKey(const Key('bank-name-error')), findsNothing);
    });

    testWidgets('clears a field message as soon as the worker types again', (
      tester,
    ) async {
      await _pump(tester, FakeWorkerBank(_none));
      await _save(tester);
      expect(find.byKey(const Key('bank-account-error')), findsOneWidget);

      await _fill(tester, account: '1');
      await tester.pump();

      expect(find.byKey(const Key('bank-account-error')), findsNothing);
    });

    testWidgets(
      'shows the message of the server for a 400 and keeps what was typed',
      (tester) async {
        final service = FakeWorkerBank(_none)
          ..saveError = const ApiException(
            400,
            'x',
            fieldErrors: {
              'bankAccountNo': ['Bank account number must be 6 to 30 digits.'],
            },
          );
        await _pump(tester, service);

        await _fill(tester, name: 'ACB', account: '123456');
        await _save(tester);

        expect(
          _text(tester, 'bank-error'),
          'Bank account number must be 6 to 30 digits.',
        );
        expect(_value(tester, 'bank-account'), '123456');
        expect(_text(tester, 'bank-summary-status'), 'Chưa có');
      },
    );

    testWidgets('explains a 403 without a retry button and hides the form', (
      tester,
    ) async {
      final service = FakeWorkerBank(_none)
        ..loadError = const ApiException(403, 'Forbidden');
      await _pump(tester, service);

      expect(find.textContaining('không có số dư riêng'), findsOneWidget);
      expect(find.text('Thử lại'), findsNothing);
      expect(find.byKey(const Key('bank-save')), findsNothing);
    });

    testWidgets('retries a network failure and then shows the form', (
      tester,
    ) async {
      final service = FakeWorkerBank(_none)
        ..loadError = const ApiException(0, 'Không kết nối được máy chủ.');
      await _pump(tester, service);
      expect(find.text('Thử lại'), findsOneWidget);

      service.loadError = null;
      await tester.tap(find.text('Thử lại'));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('bank-save')), findsOneWidget);
    });
  });

  group('BankAccountScreen change request (LEDGER-28, MOB-BANK-01)', () {
    Future<void> openChange(WidgetTester tester) async {
      await tester.ensureVisible(find.byKey(const Key('bank-open-change')));
      await tester.tap(find.byKey(const Key('bank-open-change')));
      await tester.pumpAndSettle();
    }

    testWidgets('opens an empty form with the 15-day warning and a way back', (
      tester,
    ) async {
      await _pump(tester, FakeWorkerBank(_acb));

      await openChange(tester);

      expect(find.byKey(const Key('bank-change-warning')), findsOneWidget);
      expect(find.textContaining('15 ngày không đổi được nữa'), findsOneWidget);
      expect(_value(tester, 'bank-name'), '');
      expect(_value(tester, 'bank-account'), '');
      expect(find.text('Gửi đơn đổi tài khoản'), findsWidgets);

      await tester.ensureVisible(find.byKey(const Key('bank-cancel-change')));
      await tester.tap(find.byKey(const Key('bank-cancel-change')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('bank-name')), findsNothing);
      expect(find.byKey(const Key('bank-open-change')), findsOneWidget);
    });

    testWidgets(
      'checks the fields before asking to confirm and sends nothing',
      (tester) async {
        final service = FakeWorkerBank(_acb);
        await _pump(tester, service);
        await openChange(tester);

        await _save(tester);

        expect(find.byKey(const Key('bank-confirm')), findsNothing);
        expect(
          _text(tester, 'bank-name-error'),
          contains('Hãy nhập tên ngân hàng'),
        );
        expect(service.requests, isEmpty);
      },
    );

    testWidgets('"Xem lại" in the confirmation sends nothing', (tester) async {
      final service = FakeWorkerBank(_acb);
      await _pump(tester, service);
      await openChange(tester);
      await _fill(tester, name: 'Techcombank', account: '8800 7700 66');

      await _save(tester);
      expect(find.byKey(const Key('bank-confirm')), findsOneWidget);
      expect(find.textContaining('15 ngày không đổi được nữa'), findsWidgets);
      await tester.tap(find.byKey(const Key('bank-confirm-no')));
      await tester.pumpAndSettle();

      expect(service.requests, isEmpty);
      expect(find.byKey(const Key('bank-name')), findsOneWidget);
    });

    testWidgets(
      'confirming sends the request once and shows the pending state with the account still in use',
      (tester) async {
        final service = FakeWorkerBank(_acb);
        await _pump(tester, service);
        await openChange(tester);
        await _fill(tester, name: ' Techcombank ', account: '8800 7700 66');

        await _save(tester);
        await tester.tap(find.byKey(const Key('bank-confirm-yes')));
        await tester.pumpAndSettle();

        expect(service.requests, [
          (name: 'Techcombank', account: '8800770066'),
        ]);
        expect(service.saved, isEmpty);
        expect(find.byKey(const Key('bank-pending')), findsOneWidget);
        expect(
          _text(tester, 'bank-pending-account'),
          'Tài khoản mới: Techcombank · •••• 0066',
        );
        expect(
          _text(tester, 'bank-summary-title'),
          'ACB',
        ); // the current account stays
        expect(find.byKey(const Key('bank-notice')), findsOneWidget);
        expect(find.byKey(const Key('bank-save')), findsNothing);
        expect(find.byKey(const Key('bank-open-change')), findsNothing);
      },
    );

    testWidgets(
      'a pending request shows when it was sent and that it waits for the Admin',
      (tester) async {
        await _pump(tester, FakeWorkerBank(_pending));

        expect(
          _text(tester, 'bank-pending-note'),
          'Đơn đổi tài khoản đã gửi ngày 13/11/2026, đang chờ Admin duyệt.',
        );
        expect(find.byKey(const Key('bank-open-change')), findsNothing);
        expect(find.byKey(const Key('bank-name')), findsNothing);
      },
    );

    testWidgets(
      'during the lock it says when a new request can be sent and what the Admin decided (approved)',
      (tester) async {
        await _pump(
          tester,
          FakeWorkerBank(
            _locked(
              BankChangeLast(
                status: 'APPROVED',
                requestedAt: DateTime.utc(2026, 11, 12, 18, 10),
                reviewedAt: DateTime.utc(2026, 11, 12, 19, 0),
              ),
            ),
          ),
        );

        expect(
          _text(tester, 'bank-lock-note'),
          'Bạn đã gửi đơn đổi tài khoản ngày 13/11/2026. Có thể gửi đơn mới từ ngày 28/11/2026.',
        );
        expect(_text(tester, 'bank-last-decision'), contains('đã được duyệt'));
        expect(find.byKey(const Key('bank-open-change')), findsNothing);
        expect(find.byKey(const Key('bank-name')), findsNothing);
      },
    );

    testWidgets('after a rejection the lock still shows, with the reason', (
      tester,
    ) async {
      await _pump(
        tester,
        FakeWorkerBank(
          _locked(
            BankChangeLast(
              status: 'REJECTED',
              rejectReason: 'Sai tên chủ tài khoản',
              requestedAt: DateTime.utc(2026, 11, 12, 18, 10),
            ),
          ),
        ),
      );

      expect(_text(tester, 'bank-lock-note'), contains('28/11/2026'));
      expect(
        _text(tester, 'bank-last-decision'),
        'Đơn gần nhất bị từ chối: Sai tên chủ tài khoản',
      );
    });

    testWidgets('a 409 says what happened and loads the real state again', (
      tester,
    ) async {
      final service = FakeWorkerBank(_acb);
      await _pump(tester, service);
      await openChange(tester);
      await _fill(tester, name: 'Techcombank', account: '8800770066');
      service.requestError = const ApiException(
        409,
        'x',
        code: 'BANK_CHANGE_PENDING',
      );
      service.bank = _pending; // what the server holds now
      final loadsBefore = service.loads;

      await _save(tester);
      await tester.tap(find.byKey(const Key('bank-confirm-yes')));
      await tester.pumpAndSettle();

      expect(service.loads, greaterThan(loadsBefore));
      expect(find.byKey(const Key('bank-pending')), findsOneWidget);
    });

    testWidgets(
      'the 409 of the lock shows the message of the server with its date',
      (tester) async {
        final service = FakeWorkerBank(_acb);
        await _pump(tester, service);
        await openChange(tester);
        await _fill(tester, name: 'Techcombank', account: '8800770066');
        service.requestError = const ApiException(
          409,
          'Bạn đã gửi đơn đổi tài khoản gần đây. Có thể gửi đơn mới từ ngày 28/11/2026.',
          code: 'BANK_CHANGE_LOCKED',
        );

        await _save(tester);
        await tester.tap(find.byKey(const Key('bank-confirm-yes')));
        await tester.pumpAndSettle();

        expect(find.textContaining('28/11/2026'), findsWidgets);
        expect(service.requests, hasLength(1));
      },
    );

    testWidgets(
      'the account must differ: the server 400 message shows and the typed values stay',
      (tester) async {
        final service = FakeWorkerBank(_acb);
        await _pump(tester, service);
        await openChange(tester);
        await _fill(tester, name: 'ACB', account: '0011001234567');
        service.requestError = const ApiException(
          400,
          'x',
          fieldErrors: {
            'bankAccountNo': ['Tài khoản mới phải khác tài khoản hiện tại.'],
          },
        );

        await _save(tester);
        await tester.tap(find.byKey(const Key('bank-confirm-yes')));
        await tester.pumpAndSettle();

        expect(
          _text(tester, 'bank-error'),
          'Tài khoản mới phải khác tài khoản hiện tại.',
        );
        expect(_value(tester, 'bank-account'), '0011001234567');
      },
    );
  });

  testWidgets(
    'the wallet screen opens the bank account screen from its button',
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

      await tester.ensureVisible(find.byKey(const Key('wallet-bank')));
      await tester.tap(find.byKey(const Key('wallet-bank')));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 500));

      expect(find.byType(BankAccountScreen), findsOneWidget);
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
