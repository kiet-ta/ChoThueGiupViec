import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/booking/models/booking_models.dart';
import 'package:mobile/features/booking/screens/order_detail_screen.dart';
import 'package:mobile/features/booking/screens/order_history_screen.dart';
import 'package:mobile/features/booking/services/fallback_service.dart';
import 'package:mobile/features/booking/services/order_service.dart';
import 'package:mobile/features/payments/models/payment_models.dart';

import 'fallback_prompt_test.dart' show FakeFallbackService;

const _poll = Duration(seconds: 5);

const _options = BookingOptions(
  serviceTiers: ['ECONOMY', 'PREMIUM'],
  shifts: [],
  premiumMinLeadHours: 4,
  shiftMaxHours: 4,
  standardMaxAreaM2: 80,
  paymentQrExpiryMinutes: 15,
  sandbox: true,
);

BookingOrder _order(String status, {String? cancelReason}) => BookingOrder(
  orderId: 100,
  orderCode: 'GV261014ABC123',
  serviceTier: ServiceTier.economy,
  addressId: 1,
  scheduledDate: '2026-10-15',
  shiftCode: 'SHIFT_MORNING',
  shiftStartAt: null,
  shiftEndAt: null,
  areaSnapshotM2: 50,
  requiredWorkers: 1,
  totalAmount: 260000,
  orderStatus: status,
  customerNote: null,
  cancelReason: cancelReason,
  paymentDeadlineAt: null,
  createdAt: null,
);

ProgressAssignment _worker(
  int id,
  String status, {
  String? name = 'Nguyễn Thị Lan',
}) => ProgressAssignment(
  assignmentId: id,
  assignmentSeq: 1,
  workerId: id + 50,
  workerName: name,
  workerRatingAvg: 4.8,
  assignmentStatus: status,
  acceptedAt: null,
  completedAt: null,
);

OrderExtension _extension(
  String status, {
  double hours = 1.5,
  int amount = 97500,
}) => OrderExtension(
  extensionId: 9,
  orderId: 100,
  workerId: 57,
  extraHours: hours,
  extraAmount: amount,
  extStatus: status,
  workerDecision: 'PENDING',
  requestedAt: null,
  decidedAt: null,
);

OrderSummary _summary(int id, String status) => OrderSummary(
  orderId: id,
  orderCode: 'GV$id',
  serviceTier: ServiceTier.economy,
  scheduledDate: '2026-10-15',
  shiftCode: 'SHIFT_MORNING',
  requiredWorkers: 1,
  totalAmount: 260000,
  orderStatus: status,
  createdAt: null,
);

class FakeOrderService implements IOrderService {
  final List<String?> statusesAsked = [];
  BookingOptions options = _options;
  String status = OrderStatus.dispatching;
  String? cancelReason;
  List<ProgressAssignment> workers = [];
  OrderExtension? extension;

  Object? loadError;
  Object? optionsError;
  Object? cancelError;
  Object? extensionError;
  Completer<BookingOrder>? cancelGate;

  int orderReads = 0;
  final cancels = <String>[];
  final extensions = <({int assignmentId, double hours})>[];

  // History
  List<OrderSummary> all = [];
  Object? listError;
  final pagesAsked = <int>[];

  @override
  Future<BookingOrder> getOrder(int orderId) {
    orderReads++;
    return loadError != null
        ? Future.error(loadError!)
        : Future.value(_order(status, cancelReason: cancelReason));
  }

  @override
  Future<OrderProgress> getProgress(int orderId) => loadError != null
      ? Future.error(loadError!)
      : Future.value(
          OrderProgress(
            orderId: orderId,
            orderStatus: status,
            requiredWorkers: 1,
            assignments: workers,
            extension: extension,
          ),
        );

  @override
  Future<BookingOptions> getOptions() => optionsError != null
      ? Future.error(optionsError!)
      : Future.value(options);

  @override
  Future<BookingOrder> cancelOrder(int orderId, String reason) {
    cancels.add(reason);
    if (cancelGate != null) return cancelGate!.future;
    if (cancelError != null) return Future.error(cancelError!);
    status = OrderStatus.cancelled;
    cancelReason = reason;
    return Future.value(_order(status, cancelReason: reason));
  }

  @override
  Future<OrderExtension> requestExtension({
    required int orderId,
    required int assignmentId,
    required double extraHours,
  }) {
    extensions.add((assignmentId: assignmentId, hours: extraHours));
    if (extensionError != null) return Future.error(extensionError!);
    extension = _extension(
      ExtensionStatus.pendingPayment,
      hours: extraHours,
      amount: 65000,
    );
    return Future.value(extension!);
  }

  @override
  Future<OrderPage> listOrders({
    int page = 1,
    int pageSize = 20,
    String? status,
  }) {
    pagesAsked.add(page);
    statusesAsked.add(status);
    if (listError != null) return Future.error(listError!);
    final matching = status == null
        ? all
        : all.where((o) => o.orderStatus == status).toList();
    final items = matching.skip((page - 1) * pageSize).take(pageSize).toList();
    return Future.value(
      OrderPage(
        items: items,
        page: page,
        pageSize: pageSize,
        total: matching.length,
      ),
    );
  }
}

void _phone(WidgetTester tester) {
  tester.view.physicalSize = const Size(390, 3200);
  tester.view.devicePixelRatio = 1.0;
  addTearDown(() {
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
}

Future<List<PaymentScreenArgs>> pumpDetail(
  WidgetTester tester,
  FakeOrderService service, {
  bool withArgs = true,
  IFallbackService? fallback,
}) async {
  _phone(tester);
  final paid = <PaymentScreenArgs>[];
  await tester.pumpWidget(
    MaterialApp(
      theme: NordicTheme.lightTheme,
      home: OrderDetailScreen(
        args: withArgs ? const OrderDetailArgs(orderId: 100) : null,
        service: service,
        fallbackService: fallback,
        pollInterval: _poll,
        onPay: paid.add,
      ),
    ),
  );
  await tester.pumpAndSettle();
  // The polling timer must not outlive the test.
  addTearDown(() => tester.pumpWidget(const SizedBox.shrink()));
  return paid;
}

Future<void> tapKey(WidgetTester tester, String key) async {
  await tester.ensureVisible(find.byKey(Key(key)));
  await tester.pumpAndSettle();
  await tester.tap(find.byKey(Key(key)));
  await tester.pumpAndSettle();
}

String textOf(WidgetTester tester, String key) =>
    tester.widget<Text>(find.byKey(Key(key))).data!;

void main() {
  group('OrderDetailScreen: BR-02 fallback question', () {
    testWidgets('an AWAITING_FALLBACK order shows the question', (
      tester,
    ) async {
      final fallback = FakeFallbackService();
      final service = FakeOrderService()..status = OrderStatus.awaitingFallback;
      await pumpDetail(tester, service, fallback: fallback);

      expect(textOf(tester, 'order-status'), 'Chờ bạn chọn: 1 người làm 2 ca');
      expect(find.byKey(const Key('fallback-accept')), findsOneWidget);
    });

    testWidgets('answering reloads the order', (tester) async {
      final fallback = FakeFallbackService();
      final service = FakeOrderService()..status = OrderStatus.awaitingFallback;
      await pumpDetail(tester, service, fallback: fallback);
      final readsBefore = service.orderReads;

      await tapKey(tester, 'fallback-accept');

      expect(fallback.answers, [true]);
      expect(service.orderReads, greaterThan(readsBefore));
    });

    testWidgets('a dispatching order has no question', (tester) async {
      await pumpDetail(
        tester,
        FakeOrderService(),
        fallback: FakeFallbackService(),
      );

      expect(find.byKey(const Key('fallback-card')), findsNothing);
    });
  });

  group('OrderDetailScreen: tracking (MOB-M2-04)', () {
    testWidgets(
      'shows the order, its timeline and that a worker is being found',
      (tester) async {
        await pumpDetail(tester, FakeOrderService());

        expect(find.text('Chi tiết đơn'), findsOneWidget);
        expect(textOf(tester, 'order-code'), 'Mã đơn GV261014ABC123');
        expect(textOf(tester, 'order-status'), 'Đang tìm người làm');
        expect(textOf(tester, 'order-schedule'), '15/10/2026 · Ca sáng');
        expect(textOf(tester, 'order-amount'), '260.000 đ');
        expect(find.byKey(const Key('order-timeline')), findsOneWidget);
        expect(
          textOf(tester, 'no-assignments'),
          contains('Đang tìm người làm'),
        );
        expect(find.byKey(const Key('pay-order')), findsNothing);
      },
    );

    testWidgets('shows each worker with name, rating and status', (
      tester,
    ) async {
      final service = FakeOrderService()
        ..status = OrderStatus.assigned
        ..workers = [
          _worker(7, AssignmentStatus.checkedIn),
          _worker(8, AssignmentStatus.assigned, name: null),
        ];
      await pumpDetail(tester, service);

      expect(find.text('Nguyễn Thị Lan'), findsOneWidget);
      expect(find.text('4,8'), findsNWidgets(2));
      expect(textOf(tester, 'assignment-status-7'), 'Đã đến nơi');
      expect(textOf(tester, 'assignment-status-8'), 'Đã nhận ca');
    });

    testWidgets(
      'shows assigned when progress has an assigned worker but order status still dispatching',
      (tester) async {
        final service = FakeOrderService()
          ..status = OrderStatus.dispatching
          ..workers = [_worker(7, AssignmentStatus.assigned)];
        await pumpDetail(tester, service);

        expect(textOf(tester, 'order-status'), 'Đã có người làm');
        expect(textOf(tester, 'assignment-status-7'), 'Đã nhận ca');
        expect(find.text('Đang tìm người làm'), findsNothing);
      },
    );

    Finder timelineTicks() => find.descendant(
      of: find.byKey(const Key('order-timeline')),
      matching: find.byIcon(Icons.check),
    );

    testWidgets('an assigned order ticks "Có người làm"', (tester) async {
      final assigned = FakeOrderService()
        ..status = OrderStatus.assigned
        ..workers = [_worker(7, AssignmentStatus.assigned)];
      await pumpDetail(tester, assigned);

      expect(timelineTicks(), findsNWidgets(3)); // Thanh toán, Tìm thợ, Có thợ
    });

    testWidgets('a dispatching order leaves "Tìm người làm" open', (
      tester,
    ) async {
      await pumpDetail(tester, FakeOrderService());

      expect(timelineTicks(), findsNWidgets(1)); // only Thanh toán
    });

    testWidgets('an unpaid order offers the payment of that order', (
      tester,
    ) async {
      final service = FakeOrderService()..status = OrderStatus.pendingPayment;
      final paid = await pumpDetail(tester, service);

      await tapKey(tester, 'pay-order');

      expect(paid.single.orderId, 100);
      expect(paid.single.extensionId, isNull);
      expect(paid.single.orderCode, 'GV261014ABC123');
    });

    testWidgets('asks the server again and shows what changed', (tester) async {
      final service = FakeOrderService();
      await pumpDetail(tester, service);
      expect(service.orderReads, 1);

      service
        ..status = OrderStatus.assigned
        ..workers = [_worker(7, AssignmentStatus.assigned)];
      await tester.pump(_poll);
      await tester.pump();

      expect(service.orderReads, 2);
      expect(textOf(tester, 'order-status'), 'Đã có người làm');
      expect(textOf(tester, 'assignment-status-7'), 'Đã nhận ca');
    });

    testWidgets('stops asking once the order is finished', (tester) async {
      final service = FakeOrderService();
      await pumpDetail(tester, service);

      service.status = OrderStatus.completed;
      await tester.pump(_poll);
      await tester.pump();
      final reads = service.orderReads;
      await tester.pump(_poll * 4);

      expect(textOf(tester, 'order-status'), 'Hoàn thành');
      expect(service.orderReads, reads);
      expect(find.byKey(const Key('open-cancel')), findsNothing);
    });

    testWidgets(
      'a failed refresh keeps what is on screen and the next one recovers',
      (tester) async {
        final service = FakeOrderService();
        await pumpDetail(tester, service);

        service.loadError = const ApiException(
          0,
          'Không kết nối được máy chủ.',
        );
        await tester.pump(_poll);
        await tester.pump();
        expect(textOf(tester, 'order-status'), 'Đang tìm người làm');

        service
          ..loadError = null
          ..status = OrderStatus.assigned;
        await tester.pump(_poll);
        await tester.pump();
        expect(textOf(tester, 'order-status'), 'Đã có người làm');
      },
    );

    testWidgets('a failed load shows the reason and loads again on retry', (
      tester,
    ) async {
      final service = FakeOrderService()
        ..loadError = const ApiException(404, 'Order not found.');
      await pumpDetail(tester, service);

      expect(find.textContaining('Không tìm thấy đơn này'), findsOneWidget);

      service.loadError = null;
      await tester.tap(find.text('Thử lại'));
      await tester.pumpAndSettle();
      expect(textOf(tester, 'order-status'), 'Đang tìm người làm');
    });

    testWidgets('without an order it says how to get here and asks nothing', (
      tester,
    ) async {
      final service = FakeOrderService();
      await pumpDetail(tester, service, withArgs: false);

      expect(find.textContaining('tab Đơn'), findsOneWidget);
      expect(service.orderReads, 0);
    });
  });

  group('OrderDetailScreen: cancel', () {
    testWidgets('an empty reason is refused before any request', (
      tester,
    ) async {
      final service = FakeOrderService();
      await pumpDetail(tester, service);

      await tapKey(tester, 'open-cancel');
      await tapKey(tester, 'submit-cancel');

      expect(textOf(tester, 'cancel-error'), contains('lý do'));
      expect(service.cancels, isEmpty);
    });

    testWidgets(
      'cancels with the trimmed reason and shows the cancelled order',
      (tester) async {
        final service = FakeOrderService();
        await pumpDetail(tester, service);

        await tapKey(tester, 'open-cancel');
        await tester.enterText(
          find.byKey(const Key('cancel-reason-input')),
          '  bận việc  ',
        );
        await tapKey(tester, 'submit-cancel');

        expect(service.cancels, ['bận việc']);
        expect(textOf(tester, 'order-status'), 'Đã huỷ');
        expect(textOf(tester, 'order-cancel-reason'), 'Lý do huỷ: bận việc');
        expect(find.byKey(const Key('order-notice')), findsOneWidget);
        expect(find.byKey(const Key('open-cancel')), findsNothing);
        expect(find.byKey(const Key('cancel-form')), findsNothing);
      },
    );

    testWidgets(
      'a cancelled order shows one red cross line, never a tick, and hides the workers section',
      (tester) async {
        final service = FakeOrderService()..status = OrderStatus.cancelled;
        await pumpDetail(tester, service);

        expect(find.byKey(const Key('order-timeline')), findsOneWidget);
        expect(
          find.descendant(
            of: find.byKey(const Key('order-timeline')),
            matching: find.byIcon(Icons.close_rounded),
          ),
          findsOneWidget,
        );
        expect(
          find.descendant(
            of: find.byKey(const Key('order-timeline')),
            matching: find.byIcon(Icons.check),
          ),
          findsNothing,
        );
        expect(find.text('Đơn đã huỷ'), findsWidgets);
        expect(find.byKey(const Key('no-assignments')), findsNothing);
        expect(find.byKey(const Key('order-paid-pill')), findsNothing);
        expect(find.byKey(const Key('order-rebook')), findsOneWidget);
        expect(find.byKey(const Key('order-back-to-list')), findsOneWidget);
      },
    );

    testWidgets('the order code can be copied', (tester) async {
      String? copied;
      tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
        SystemChannels.platform,
        (call) async {
          if (call.method == 'Clipboard.setData') {
            copied = (call.arguments as Map)['text'] as String?;
          }
          return null;
        },
      );
      addTearDown(
        () => tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
          SystemChannels.platform,
          null,
        ),
      );
      final service = FakeOrderService();
      await pumpDetail(tester, service);

      await tapKey(tester, 'order-code-copy');

      expect(copied, 'GV261014ABC123');
    });

    testWidgets('a second tap while cancelling sends nothing more', (
      tester,
    ) async {
      final service = FakeOrderService()
        ..cancelGate = Completer<BookingOrder>();
      await pumpDetail(tester, service);
      await tapKey(tester, 'open-cancel');
      await tester.enterText(
        find.byKey(const Key('cancel-reason-input')),
        'bận',
      );

      await tester.tap(find.byKey(const Key('submit-cancel')));
      await tester.pump();
      await tester.tap(
        find.byKey(const Key('submit-cancel')),
        warnIfMissed: false,
      );
      await tester.pump();

      expect(service.cancels.length, 1);
      service.cancelGate!.complete(
        _order(OrderStatus.cancelled, cancelReason: 'bận'),
      );
      await tester.pumpAndSettle();
      expect(textOf(tester, 'order-status'), 'Đã huỷ');
    });

    testWidgets('a refusal of the server is explained and the order stays', (
      tester,
    ) async {
      final service = FakeOrderService()
        ..status = OrderStatus.assigned
        ..cancelError = const ApiException(
          409,
          'The order can no longer be cancelled.',
          code: 'INVALID_STATE',
        );
      await pumpDetail(tester, service);

      await tapKey(tester, 'open-cancel');
      await tester.enterText(
        find.byKey(const Key('cancel-reason-input')),
        'bận',
      );
      await tapKey(tester, 'submit-cancel');

      expect(textOf(tester, 'cancel-error'), contains('không huỷ được'));
      expect(find.textContaining('can no longer'), findsNothing);
      expect(textOf(tester, 'order-status'), 'Đã có người làm');
    });
  });

  group('OrderDetailScreen: "Làm lần 2" (MOB-M2-05)', () {
    FakeOrderService working() => FakeOrderService()
      ..status = OrderStatus.assigned
      ..workers = [_worker(7, AssignmentStatus.inProgress)];

    testWidgets('is offered only while a worker is working', (tester) async {
      final service = FakeOrderService()
        ..status = OrderStatus.assigned
        ..workers = [_worker(7, AssignmentStatus.checkedIn)];
      await pumpDetail(tester, service);
      expect(find.byKey(const Key('open-extension')), findsNothing);

      service.workers = [_worker(7, AssignmentStatus.inProgress)];
      await tester.pump(_poll);
      await tester.pump();
      expect(find.byKey(const Key('open-extension')), findsOneWidget);
    });

    testWidgets('offers half hours up to the shift length of the options', (
      tester,
    ) async {
      await pumpDetail(tester, working());

      await tapKey(tester, 'open-extension');

      expect(find.byType(ChoiceChip), findsNWidgets(8));
      expect(find.byKey(const Key('hours-0.5')), findsOneWidget);
      expect(find.byKey(const Key('hours-4.0')), findsOneWidget);
      expect(find.byKey(const Key('hours-4.5')), findsNothing);
    });

    testWidgets('needs the hours before any request', (tester) async {
      final service = working();
      await pumpDetail(tester, service);

      await tapKey(tester, 'open-extension');
      await tapKey(tester, 'submit-extension');

      expect(textOf(tester, 'extension-error'), contains('số giờ'));
      expect(service.extensions, isEmpty);
    });

    testWidgets(
      'sends the worker and the hours, then shows the server price and its payment',
      (tester) async {
        final service = working();
        final paid = await pumpDetail(tester, service);

        await tapKey(tester, 'open-extension');
        await tapKey(tester, 'hours-1.0');
        await tapKey(tester, 'submit-extension');

        expect(service.extensions.single, (assignmentId: 7, hours: 1.0));
        expect(textOf(tester, 'extension-summary'), 'Thêm 1 giờ · 65.000 đ');
        expect(textOf(tester, 'extension-status'), 'Chờ thanh toán');
        expect(find.byKey(const Key('extension-form')), findsNothing);
        expect(find.byKey(const Key('open-extension')), findsNothing);

        await tapKey(tester, 'pay-extension');
        expect(paid.single.orderId, 100);
        expect(paid.single.extensionId, 9);
      },
    );

    testWidgets('with two workers on site the chosen one is sent', (
      tester,
    ) async {
      final service = working()
        ..workers = [
          _worker(7, AssignmentStatus.inProgress),
          _worker(8, AssignmentStatus.inProgress, name: 'Trần Văn Bình'),
        ];
      await pumpDetail(tester, service);

      await tapKey(tester, 'open-extension');
      await tapKey(tester, 'extension-worker-8');
      await tapKey(tester, 'hours-0.5');
      await tapKey(tester, 'submit-extension');

      expect(service.extensions.single, (assignmentId: 8, hours: 0.5));
    });

    testWidgets('EXTENSION_EXISTS is explained from its code', (tester) async {
      final service = working()
        ..extensionError = const ApiException(
          409,
          'This order already has an extension.',
          code: 'EXTENSION_EXISTS',
        );
      await pumpDetail(tester, service);

      await tapKey(tester, 'open-extension');
      await tapKey(tester, 'hours-1.0');
      await tapKey(tester, 'submit-extension');

      expect(textOf(tester, 'extension-error'), contains('đã có'));
      expect(find.textContaining('already has'), findsNothing);
    });

    testWidgets(
      'an extension that is no longer waiting for payment has no pay button',
      (tester) async {
        final service = working()
          ..extension = _extension(ExtensionStatus.declined);
        await pumpDetail(tester, service);

        expect(textOf(tester, 'extension-summary'), 'Thêm 1,5 giờ · 97.500 đ');
        expect(textOf(tester, 'extension-status'), contains('từ chối'));
        expect(find.byKey(const Key('pay-extension')), findsNothing);
        expect(find.byKey(const Key('open-extension')), findsNothing);
      },
    );

    testWidgets(
      'when the options cannot be read the form says so and can retry',
      (tester) async {
        final service = working()
          ..optionsError = const ApiException(500, 'boom');
        await pumpDetail(tester, service);

        await tapKey(tester, 'open-extension');
        expect(textOf(tester, 'extension-error'), contains('Máy chủ gặp lỗi'));
        expect(find.byKey(const Key('submit-extension')), findsNothing);

        service.optionsError = null;
        await tester.tap(find.text('Thử lại'));
        await tester.pumpAndSettle();
        expect(find.byKey(const Key('hours-0.5')), findsOneWidget);
      },
    );
  });

  group('OrderHistoryScreen (MOB-M2-06)', () {
    Future<List<OrderSummary>> pumpHistory(
      WidgetTester tester,
      FakeOrderService service, {
      VoidCallback? onBook,
      DateTime Function()? clock,
    }) async {
      tester.view.physicalSize = const Size(390, 6000);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });
      final opened = <OrderSummary>[];
      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: OrderHistoryScreen(
            service: service,
            onOpenOrder: opened.add,
            onBook: onBook,
            clock: clock,
          ),
        ),
      );
      await tester.pumpAndSettle();
      return opened;
    }

    testWidgets('lists the orders with code, status, schedule and amount', (
      tester,
    ) async {
      final service = FakeOrderService()
        ..all = [
          _summary(1, OrderStatus.assigned),
          _summary(2, OrderStatus.cancelled),
        ];
      await pumpHistory(tester, service);

      expect(find.text('Mã GV1'), findsOneWidget);
      expect(textOf(tester, 'order-status-1'), 'Đã có người làm');
      expect(textOf(tester, 'order-status-2'), 'Đã huỷ');
      expect(find.text('Ca sáng · Cơ bản'), findsNWidgets(2));
      expect(find.text('260.000 đ'), findsNWidgets(2));
      expect(find.text('Tháng 10/2026'), findsOneWidget);
      expect(textOf(tester, 'history-count'), '2 đơn');
      expect(find.byKey(const Key('history-more')), findsNothing);
    });

    testWidgets(
      'a month is listed once, newest month first, when the server interleaves them',
      (tester) async {
        OrderSummary on(int id, String date) => OrderSummary(
          orderId: id,
          orderCode: 'GV$id',
          serviceTier: ServiceTier.economy,
          scheduledDate: date,
          shiftCode: 'SHIFT_MORNING',
          requiredWorkers: 1,
          totalAmount: 260000,
          orderStatus: OrderStatus.completed,
          createdAt: null,
        );
        final service = FakeOrderService()
          ..all = [
            on(3, '2026-10-12'),
            on(2, '2026-11-02'),
            on(1, '2026-10-05'),
          ];
        await pumpHistory(tester, service);

        expect(find.text('Tháng 10/2026'), findsOneWidget);
        expect(find.text('Tháng 11/2026'), findsOneWidget);
        expect(
          tester.getTopLeft(find.text('Tháng 11/2026')).dy,
          lessThan(tester.getTopLeft(find.text('Tháng 10/2026')).dy),
        );
      },
    );

    testWidgets(
      'a filter chip asks the server for that status and says so when there is none',
      (tester) async {
        final service = FakeOrderService()
          ..all = [_summary(1, OrderStatus.completed)];
        await pumpHistory(tester, service);

        await tapKey(tester, 'history-filter-CANCELLED');

        expect(service.statusesAsked.last, OrderStatus.cancelled);
        expect(find.text('Không có đơn đã huỷ'), findsOneWidget);
        expect(textOf(tester, 'history-count'), '0 đơn');

        await tapKey(tester, 'history-filter-all');

        expect(service.statusesAsked.last, isNull);
        expect(find.byKey(const Key('order-1')), findsOneWidget);
      },
    );

    testWidgets(
      'an open order that has not started is also shown as a dark card on "Tất cả" only',
      (tester) async {
        final service = FakeOrderService()
          ..options = const BookingOptions(
            serviceTiers: ['ECONOMY', 'PREMIUM'],
            shifts: [
              ShiftOption(
                shiftCode: 'SHIFT_MORNING',
                startLocal: '08:00',
                endLocal: '12:00',
              ),
            ],
            premiumMinLeadHours: 4,
            shiftMaxHours: 4,
            standardMaxAreaM2: 80,
            paymentQrExpiryMinutes: 15,
            sandbox: true,
          )
          ..all = [
            _summary(1, OrderStatus.assigned),
            _summary(2, OrderStatus.completed),
          ];
        // 14/10/2026 10:00 in Ho Chi Minh: the shift of 15/10 08:00 has not started.
        await pumpHistory(
          tester,
          service,
          clock: () => DateTime.utc(2026, 10, 14, 3),
        );

        expect(find.text('Sắp tới'), findsOneWidget);
        expect(find.byKey(const Key('history-upcoming-1')), findsOneWidget);
        expect(find.byKey(const Key('history-upcoming-2')), findsNothing);
        expect(
          find.byKey(const Key('order-1')),
          findsOneWidget,
        ); // also in its month
        expect(find.text('08:00–12:00 · 260.000 đ'), findsNWidgets(2));

        await tapKey(tester, 'history-filter-COMPLETED');

        expect(find.text('Sắp tới'), findsNothing);
      },
    );

    testWidgets('a tap opens that order', (tester) async {
      final service = FakeOrderService()
        ..all = [
          _summary(1, OrderStatus.assigned),
          _summary(2, OrderStatus.completed),
        ];
      final opened = await pumpHistory(tester, service);

      await tapKey(tester, 'order-2');

      expect(opened.single.orderId, 2);
    });

    testWidgets('"Xem thêm" loads the next page until everything is shown', (
      tester,
    ) async {
      final service = FakeOrderService()
        ..all = [
          for (var i = 1; i <= 25; i++) _summary(i, OrderStatus.completed),
        ];
      await pumpHistory(tester, service);

      expect(textOf(tester, 'history-count'), '20 / 25 đơn');
      expect(find.byKey(const Key('order-21')), findsNothing);

      await tapKey(tester, 'history-more');

      expect(service.pagesAsked, [1, 2]);
      expect(textOf(tester, 'history-count'), '25 đơn');
      expect(find.byKey(const Key('order-25')), findsOneWidget);
      expect(find.byKey(const Key('history-more')), findsNothing);
    });

    testWidgets('a failed next page keeps the list and can be tried again', (
      tester,
    ) async {
      final service = FakeOrderService()
        ..all = [
          for (var i = 1; i <= 25; i++) _summary(i, OrderStatus.completed),
        ];
      await pumpHistory(tester, service);

      service.listError = const ApiException(500, 'boom');
      await tapKey(tester, 'history-more');
      expect(textOf(tester, 'history-more-error'), contains('Máy chủ gặp lỗi'));
      expect(find.byKey(const Key('order-20')), findsOneWidget);

      service.listError = null;
      await tapKey(tester, 'history-more');
      expect(textOf(tester, 'history-count'), '25 đơn');
    });

    testWidgets('without any order it offers to book', (tester) async {
      var booked = 0;
      await pumpHistory(tester, FakeOrderService(), onBook: () => booked++);

      expect(find.text('Bạn chưa có đơn nào'), findsOneWidget);
      await tester.tap(find.text('Đặt ca'));
      await tester.pumpAndSettle();
      expect(booked, 1);
    });

    testWidgets('a failed load shows the reason and loads again on retry', (
      tester,
    ) async {
      final service = FakeOrderService()
        ..all = [_summary(1, OrderStatus.assigned)]
        ..listError = const ApiException(0, 'Không kết nối được máy chủ.');
      await pumpHistory(tester, service);

      expect(find.text('Không kết nối được máy chủ.'), findsOneWidget);

      service.listError = null;
      await tester.tap(find.text('Thử lại'));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('order-1')), findsOneWidget);
    });
  });
}
