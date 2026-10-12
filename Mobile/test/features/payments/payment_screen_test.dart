import 'dart:async';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/payments/models/payment_models.dart';
import 'package:mobile/features/payments/screens/payment_screen.dart';
import 'package:mobile/features/payments/services/payments_service.dart';

final _start = DateTime.utc(2026, 10, 14, 3);

Payment _payment({String status = 'PENDING', String? qr = 'fake-qr://FAKE-1', String? payUrl = 'https://sandbox.invalid/pay/FAKE-1', String purpose = 'ORDER', int amount = 260000}) =>
    Payment(
      paymentId: 5,
      purpose: purpose,
      orderId: purpose == 'ORDER' ? 42 : null,
      extensionId: purpose == 'EXTENSION' ? 9 : null,
      gateway: 'FAKE',
      amount: amount,
      txnStatus: status,
      qrPayload: status == 'PENDING' ? qr : null,
      payUrl: payUrl,
      expiresAt: _start.add(const Duration(minutes: 15)),
      paidAt: status == 'SUCCESS' ? _start.add(const Duration(minutes: 1)) : null,
      createdAt: _start,
      sandbox: true,
    );

class FakePaymentsService implements IPaymentsService {
  Object? createError;
  Completer<Payment>? createGate;
  Payment created = _payment();
  final createdOrders = <int>[];
  final createdExtensions = <int>[];

  /// What the next status checks answer, in order; the last one repeats.
  final List<Object> polls = [];
  int pollCalls = 0;

  @override
  Future<Payment> createOrderQr(int orderId) {
    createdOrders.add(orderId);
    if (createGate != null) return createGate!.future;
    return createError != null ? Future.error(createError!) : Future.value(created);
  }

  @override
  Future<Payment> createExtensionQr(int extensionId) {
    createdExtensions.add(extensionId);
    return createError != null ? Future.error(createError!) : Future.value(created);
  }

  @override
  Future<Payment> getPayment(int paymentId) {
    final answer = polls.isEmpty ? created : polls[pollCalls < polls.length ? pollCalls : polls.length - 1];
    pollCalls++;
    return answer is Payment ? Future.value(answer) : Future.error(answer);
  }
}

/// A clock the test moves forward together with the fake timers.
class TestClock {
  DateTime now = _start;
  void advance(Duration d) => now = now.add(d);
}

Future<List<Payment>> pumpPayment(
  WidgetTester tester,
  FakePaymentsService service,
  TestClock clock, {
  PaymentScreenArgs? args = const PaymentScreenArgs(orderId: 42, orderCode: 'GV261014ABC123'),
}) async {
  tester.view.physicalSize = const Size(390, 1800);
  tester.view.devicePixelRatio = 1.0;
  addTearDown(() {
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
  final done = <Payment>[];
  await tester.pumpWidget(MaterialApp(
    theme: NordicTheme.lightTheme,
    home: PaymentScreen(args: args, service: service, now: () => clock.now, pollInterval: const Duration(seconds: 3), onPaid: done.add),
  ));
  await tester.pump(); // the QR request completes
  return done;
}

/// Moves the clock and the timers forward by the same amount.
Future<void> elapse(WidgetTester tester, TestClock clock, Duration d) async {
  clock.advance(d);
  await tester.pump(d);
  await tester.pump();
}

/// Removes the screen so its timers are cancelled before the test ends.
Future<void> close(WidgetTester tester) async {
  await tester.pumpWidget(const SizedBox.shrink());
  await tester.pump();
}

String textOf(WidgetTester tester, String key) => tester.widget<Text>(find.byKey(Key(key))).data!;

void main() {
  group('PaymentScreen (MOB-M2-03)', () {
    testWidgets('always shows the sandbox banner, also while loading and on an error', (tester) async {
      final service = FakePaymentsService()..createGate = Completer<Payment>();
      await pumpPayment(tester, service, TestClock());

      expect(find.text('SANDBOX – no real money'), findsOneWidget);
      expect(find.text('Đang tạo mã thanh toán…'), findsOneWidget);

      service.createGate!.completeError(const ApiException(502, 'gateway down'));
      await tester.pump();
      await tester.pump();

      expect(find.text('SANDBOX – no real money'), findsOneWidget);
      expect(find.textContaining('Chưa có gì bị trừ'), findsOneWidget);
      await close(tester);
    });

    testWidgets('asks the QR of the order once and shows the amount, the QR content, the pay URL and the time left', (tester) async {
      final service = FakePaymentsService();
      await pumpPayment(tester, service, TestClock());

      expect(service.createdOrders, [42]);
      expect(service.createdExtensions, isEmpty);
      expect(find.text('Thanh toán GV261014ABC123'), findsOneWidget);
      expect(textOf(tester, 'payment-amount'), '260.000 đ');
      expect(tester.widget<SelectableText>(find.byKey(const Key('payment-qr-payload'))).data, 'fake-qr://FAKE-1');
      expect(tester.widget<SelectableText>(find.byKey(const Key('payment-pay-url'))).data, 'https://sandbox.invalid/pay/FAKE-1');
      expect(textOf(tester, 'payment-countdown'), '15:00');
      expect(find.textContaining('chưa vẽ được mã QR dạng hình'), findsOneWidget);
      await close(tester);
    });

    testWidgets('counts down every second', (tester) async {
      final clock = TestClock();
      await pumpPayment(tester, FakePaymentsService(), clock);

      await elapse(tester, clock, const Duration(seconds: 1));
      expect(textOf(tester, 'payment-countdown'), '14:59');
      await elapse(tester, clock, const Duration(seconds: 1));
      expect(textOf(tester, 'payment-countdown'), '14:58');
      await close(tester);
    });

    testWidgets('polls the status and switches to "Đã thanh toán" when the money arrives, then stops polling', (tester) async {
      final clock = TestClock();
      final service = FakePaymentsService()..polls.addAll([_payment(), _payment(status: 'SUCCESS')]);
      final done = await pumpPayment(tester, service, clock);

      await elapse(tester, clock, const Duration(seconds: 3));
      expect(service.pollCalls, 1);
      expect(find.byKey(const Key('payment-paid')), findsNothing);

      await elapse(tester, clock, const Duration(seconds: 3));
      expect(service.pollCalls, 2);
      expect(find.byKey(const Key('payment-paid')), findsOneWidget);
      expect(find.textContaining('Đã nhận 260.000 đ'), findsOneWidget);
      expect(find.byKey(const Key('payment-qr-payload')), findsNothing);

      await elapse(tester, clock, const Duration(seconds: 30));
      expect(service.pollCalls, 2); // no more requests once paid

      await tester.tap(find.text('Xem đơn'));
      await tester.pump();
      expect(done.single.txnStatus, 'SUCCESS');
    });

    testWidgets('when the server says EXPIRED it shows that and stops polling', (tester) async {
      final clock = TestClock();
      final service = FakePaymentsService()..polls.add(_payment(status: 'EXPIRED'));
      await pumpPayment(tester, service, clock);

      await elapse(tester, clock, const Duration(seconds: 3));
      expect(find.byKey(const Key('payment-expired')), findsOneWidget);
      expect(find.textContaining('không bị trừ tiền'), findsOneWidget);

      await elapse(tester, clock, const Duration(seconds: 30));
      expect(service.pollCalls, 1);
    });

    testWidgets('at the deadline it stops offering the QR even if the server still says PENDING', (tester) async {
      final clock = TestClock();
      final service = FakePaymentsService();
      await pumpPayment(tester, service, clock);

      await elapse(tester, clock, const Duration(minutes: 14, seconds: 59));
      expect(find.byKey(const Key('payment-countdown')), findsOneWidget);

      await elapse(tester, clock, const Duration(seconds: 2));
      expect(find.byKey(const Key('payment-expired')), findsOneWidget);
      final calls = service.pollCalls;

      await elapse(tester, clock, const Duration(seconds: 30));
      expect(service.pollCalls, calls); // nothing is asked after the deadline
    });

    testWidgets('a failed status check keeps the QR on screen and tries again; after three it says so', (tester) async {
      final clock = TestClock();
      final service = FakePaymentsService()
        ..polls.addAll([
          const ApiException(0, 'offline'),
          const ApiException(0, 'offline'),
          const ApiException(0, 'offline'),
          _payment(status: 'SUCCESS'),
        ]);
      await pumpPayment(tester, service, clock);

      await elapse(tester, clock, const Duration(seconds: 3));
      await elapse(tester, clock, const Duration(seconds: 3));
      expect(find.byKey(const Key('payment-qr-payload')), findsOneWidget);
      expect(textOf(tester, 'payment-waiting-note'), contains('Đang chờ thanh toán'));

      await elapse(tester, clock, const Duration(seconds: 3));
      expect(textOf(tester, 'payment-waiting-note'), contains('Chưa kiểm tra được'));

      await elapse(tester, clock, const Duration(seconds: 3));
      expect(find.byKey(const Key('payment-paid')), findsOneWidget);
    });

    testWidgets('keeps the pay URL when a later status answer has none', (tester) async {
      final clock = TestClock();
      final service = FakePaymentsService()..polls.add(_payment(payUrl: null));
      await pumpPayment(tester, service, clock);

      await elapse(tester, clock, const Duration(seconds: 3));

      expect(tester.widget<SelectableText>(find.byKey(const Key('payment-pay-url'))).data, 'https://sandbox.invalid/pay/FAKE-1');
      await close(tester);
    });

    testWidgets('PAYMENT_EXPIRED and INVALID_STATE are explained from their codes, with a retry', (tester) async {
      final service = FakePaymentsService()..createError = const ApiException(409, 'server text', code: 'PAYMENT_EXPIRED');
      await pumpPayment(tester, service, TestClock());

      expect(find.textContaining('quá hạn thanh toán'), findsOneWidget);
      expect(find.textContaining('server text'), findsNothing);

      service.createError = null;
      await tester.tap(find.text('Thử lại'));
      await tester.pump();
      await tester.pump();

      expect(service.createdOrders, [42, 42]);
      expect(find.byKey(const Key('payment-qr-payload')), findsOneWidget);
      await close(tester);
    });

    testWidgets('an order already paid when the screen opens is shown as paid without polling', (tester) async {
      final clock = TestClock();
      final service = FakePaymentsService()..created = _payment(status: 'SUCCESS');
      await pumpPayment(tester, service, clock);

      expect(find.byKey(const Key('payment-paid')), findsOneWidget);
      await elapse(tester, clock, const Duration(seconds: 30));
      expect(service.pollCalls, 0);
    });

    testWidgets('for an extension it asks the extension QR and names the purpose', (tester) async {
      final service = FakePaymentsService()..created = _payment(purpose: 'EXTENSION', amount: 97500);
      await pumpPayment(tester, service, TestClock(), args: const PaymentScreenArgs(orderId: 42, extensionId: 9));

      expect(service.createdExtensions, [9]);
      expect(service.createdOrders, isEmpty);
      expect(find.text('Thanh toán làm thêm giờ'), findsOneWidget);
      expect(textOf(tester, 'payment-amount'), '97.500 đ');
      await close(tester);
    });

    testWidgets('without an order it explains itself and asks nothing', (tester) async {
      final service = FakePaymentsService();
      await pumpPayment(tester, service, TestClock(), args: null);

      expect(find.textContaining('mở từ một đơn cần thanh toán'), findsOneWidget);
      expect(find.text('SANDBOX – no real money'), findsOneWidget);
      expect(service.createdOrders, isEmpty);
    });

    testWidgets('leaving the screen stops the polling', (tester) async {
      final clock = TestClock();
      final service = FakePaymentsService();
      await pumpPayment(tester, service, clock);

      await elapse(tester, clock, const Duration(seconds: 3));
      final calls = service.pollCalls;
      await close(tester);
      clock.advance(const Duration(seconds: 30));
      await tester.pump(const Duration(seconds: 30));

      expect(service.pollCalls, calls);
    });
  });
}
