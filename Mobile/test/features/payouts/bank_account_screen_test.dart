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
);

class FakeWorkerBank implements IWorkerBankService {
  WorkerBank bank;
  Object? loadError;
  Object? saveError;
  final saved = <({String name, String account})>[];
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
      'shows a saved account with its number masked and fills the form for a change',
      (tester) async {
        await _pump(tester, FakeWorkerBank(_acb));

        expect(_text(tester, 'bank-summary-title'), 'ACB');
        expect(_text(tester, 'bank-summary-account'), '•••• 4567');
        expect(_text(tester, 'bank-summary-status'), 'Đã thiết lập');
        expect(find.text('Lưu thay đổi'), findsOneWidget);
        expect(_value(tester, 'bank-name'), 'ACB');
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
        expect(find.text('Lưu thay đổi'), findsOneWidget);
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
