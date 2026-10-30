import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/booking/logic/order_logic.dart';
import 'package:mobile/features/booking/models/booking_models.dart';

ProgressAssignment _assignment(
  int id,
  String status, {
  String? name = 'Lan',
  double rating = 4.8,
}) => ProgressAssignment(
  assignmentId: id,
  assignmentSeq: 1,
  workerId: id + 100,
  workerName: name,
  workerRatingAvg: rating,
  assignmentStatus: status,
  acceptedAt: null,
  completedAt: null,
);

OrderProgress _progress(
  String orderStatus,
  List<ProgressAssignment> assignments, {
  OrderExtension? extension,
}) => OrderProgress(
  orderId: 1,
  orderStatus: orderStatus,
  requiredWorkers: assignments.length,
  assignments: assignments,
  extension: extension,
);

const _extension = OrderExtension(
  extensionId: 9,
  orderId: 1,
  workerId: 101,
  extraHours: 1.5,
  extraAmount: 97500,
  extStatus: ExtensionStatus.pendingPayment,
  workerDecision: 'PENDING',
  requestedAt: null,
  decidedAt: null,
);

void main() {
  groupTests();
  group('labels', () {
    test('every order status of the contract has a Vietnamese label; an unknown one is shown as is', () {
      for (final s in [
        'PENDING_PAYMENT',
        'PAID',
        'DISPATCHING',
        'ASSIGNED',
        'COMPLETED',
        'CANCELLED',
      ]) {
        expect(orderStatusLabel(s), isNot(s));
      }
      expect(orderStatusLabel('NEW_STATUS'), 'NEW_STATUS');
    });

    test(
      'every visible assignment status and extension status has a label',
      () {
        for (final s in [
          'ASSIGNED',
          'CHECKED_IN',
          'IN_PROGRESS',
          'AWAITING_ACCEPTANCE',
          'COMPLETED',
          'CANCELLED_BY_WORKER',
          'ABSENT',
          'INCIDENT',
        ]) {
          expect(assignmentStatusLabel(s), isNot(s));
        }
        for (final s in [
          'PENDING_PAYMENT',
          'PAID',
          'ACCEPTED',
          'DECLINED',
          'EXPIRED',
        ]) {
          expect(extensionStatusLabel(s), isNot(s));
        }
      },
    );

    test(
      'worker name falls back, rating and hours use the Vietnamese comma',
      () {
        expect(workerName(_assignment(1, 'ASSIGNED', name: null)), 'Người làm');
        expect(workerName(_assignment(1, 'ASSIGNED', name: '  ')), 'Người làm');
        expect(workerName(_assignment(1, 'ASSIGNED')), 'Lan');
        expect(ratingText(4.8), '4,8');
        expect(ratingText(0), '');
        expect(hoursText(1.5), '1,5 giờ');
        expect(hoursText(2), '2 giờ');
      },
    );

    test('schedule text turns the contract date around', () {
      expect(
        scheduleText('2026-10-15', 'SHIFT_MORNING'),
        '15/10/2026 · Ca sáng',
      );
      expect(scheduleText('bad', 'SHIFT_EVENING'), 'bad · Ca tối');
    });
  });

  group('orderTimeline', () {
    List<String> current(String status) => [
      for (final s in orderTimeline(status))
        if (s.current) s.label,
    ];
    List<String> done(String status) => [
      for (final s in orderTimeline(status))
        if (s.done) s.label,
    ];

    test('walks pay, find, assigned, done', () {
      expect(current('PENDING_PAYMENT'), ['Thanh toán']);
      expect(done('PENDING_PAYMENT'), isEmpty);
      expect(current('DISPATCHING'), ['Tìm người']);
      expect(done('DISPATCHING'), ['Thanh toán']);
      expect(current('ASSIGNED'), ['Có người làm']);
      expect(done('ASSIGNED'), ['Thanh toán', 'Tìm người']);
    });

    test('PAID is shown as finding a worker', () {
      expect(current('PAID'), ['Tìm người']);
    });

    test('a completed order has every step done and none current', () {
      expect(done('COMPLETED').length, 4);
      expect(current('COMPLETED'), isEmpty);
    });

    test('a cancelled order is one cancelled step, never a tick', () {
      final step = orderTimeline('CANCELLED').single;
      expect(step.label, 'Đơn đã huỷ');
      expect(step.cancelled, isTrue);
      expect(step.done, isFalse);
      expect(step.current, isTrue);
    });
  });

  group('orderStatusForDisplay', () {
    test('an active assignment advances a lagging dispatching order', () {
      expect(
        orderStatusForDisplay(OrderStatus.dispatching, [
          _assignment(1, AssignmentStatus.assigned),
        ]),
        OrderStatus.assigned,
      );
      expect(
        orderStatusForDisplay(OrderStatus.dispatching, [
          _assignment(1, AssignmentStatus.inProgress),
        ]),
        OrderStatus.assigned,
      );
    });

    test(
      'cancelled or absent assignments do not imply a worker is assigned',
      () {
        expect(
          orderStatusForDisplay(OrderStatus.dispatching, [
            _assignment(1, AssignmentStatus.cancelledByWorker),
            _assignment(2, AssignmentStatus.absent),
          ]),
          OrderStatus.dispatching,
        );
      },
    );

    test('does not override other aggregate order states', () {
      expect(
        orderStatusForDisplay(OrderStatus.cancelled, [
          _assignment(1, AssignmentStatus.assigned),
        ]),
        OrderStatus.cancelled,
      );
    });
  });

  group('what is offered', () {
    test('polling stops for finished orders only', () {
      expect(shouldPollOrder('PENDING_PAYMENT'), isTrue);
      expect(shouldPollOrder('ASSIGNED'), isTrue);
      expect(shouldPollOrder('COMPLETED'), isFalse);
      expect(shouldPollOrder('CANCELLED'), isFalse);
    });

    test('pay only while waiting for payment; cancel until finished', () {
      expect(canPay('PENDING_PAYMENT'), isTrue);
      expect(canPay('DISPATCHING'), isFalse);
      expect(canCancel('PENDING_PAYMENT'), isTrue);
      expect(canCancel('ASSIGNED'), isTrue);
      expect(canCancel('COMPLETED'), isFalse);
      expect(canCancel('CANCELLED'), isFalse);
    });

    test('an extension needs an ASSIGNED order, a worker on site and no extension yet', () {
      expect(
        canExtend(_progress('ASSIGNED', [_assignment(1, 'IN_PROGRESS')])),
        isTrue,
      );
      expect(
        canExtend(
          _progress('ASSIGNED', [_assignment(1, 'AWAITING_ACCEPTANCE')]),
        ),
        isTrue,
      );
      expect(
        canExtend(_progress('ASSIGNED', [_assignment(1, 'CHECKED_IN')])),
        isFalse,
      );
      expect(
        canExtend(_progress('ASSIGNED', [_assignment(1, 'ASSIGNED')])),
        isFalse,
      );
      expect(
        canExtend(_progress('COMPLETED', [_assignment(1, 'IN_PROGRESS')])),
        isFalse,
      );
      expect(
        canExtend(
          _progress('ASSIGNED', [
            _assignment(1, 'IN_PROGRESS'),
          ], extension: _extension),
        ),
        isFalse,
      );
    });

    test('only on-site workers can be asked', () {
      final p = _progress('ASSIGNED', [
        _assignment(1, 'IN_PROGRESS'),
        _assignment(2, 'ABSENT'),
        _assignment(3, 'AWAITING_ACCEPTANCE'),
      ]);
      expect(extendableAssignments(p).map((a) => a.assignmentId), [1, 3]);
    });

    test('hour options go by half hours up to the shift length', () {
      expect(extraHourOptions(4), [0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 3.5, 4.0]);
      expect(extraHourOptions(0), isEmpty);
    });

    test('the cancel reason is required and at most 255 characters', () {
      expect(validateCancelReason('   '), isNotNull);
      expect(validateCancelReason('bận việc'), isNull);
      expect(validateCancelReason('a' * 255), isNull);
      expect(validateCancelReason('a' * 256), contains('255'));
    });

    test('more orders exist while fewer are shown than the total', () {
      expect(hasMoreOrders(20, 21), isTrue);
      expect(hasMoreOrders(21, 21), isFalse);
    });
  });

  group('failures in words', () {
    test('a refused cancel is explained, a 400 uses the field message', () {
      expect(
        cancelFailure(const ApiException(409, 'x', code: 'INVALID_STATE')),
        contains('không huỷ được'),
      );
      expect(
        cancelFailure(
          const ApiException(
            400,
            'Validation failed',
            fieldErrors: {
              'reason': ['reason is required.'],
            },
          ),
        ),
        'reason is required.',
      );
      expect(
        cancelFailure(const ApiException(404, 'x')),
        contains('Không tìm thấy'),
      );
    });

    test('a refused extension is explained by its code, never by the server message', () {
      expect(
        extensionFailure(
          const ApiException(409, 'server text', code: 'EXTENSION_EXISTS'),
        ),
        contains('đã có'),
      );
      expect(
        extensionFailure(
          const ApiException(409, 'server text', code: 'INVALID_STATE'),
        ),
        contains('đang ở nhà bạn'),
      );
      expect(
        extensionFailure(const ApiException(409, 'server text')),
        isNot(contains('server text')),
      );
      expect(
        extensionFailure(
          const ApiException(
            400,
            'Validation failed',
            fieldErrors: {
              'extraHours': ['extraHours must be a multiple of 0.5.'],
            },
          ),
        ),
        contains('0.5'),
      );
    });

    test('network, session and role problems read the same everywhere', () {
      expect(
        orderLoadFailure(const ApiException(0, 'Không kết nối được máy chủ.')),
        'Không kết nối được máy chủ.',
      );
      expect(
        orderLoadFailure(const ApiException(401, 'x')),
        contains('đăng nhập'),
      );
      expect(
        orderLoadFailure(const ApiException(403, 'x')),
        contains('khách hàng'),
      );
      expect(
        orderLoadFailure(const ApiException(500, 'x')),
        contains('Máy chủ gặp lỗi'),
      );
    });
  });

  group('cancelReasonView', () {
    test('the no-worker reason reads kindly and offers another time', () {
      final view = cancelReasonView(
        'Quá bán kính 10 km không tìm được thợ phù hợp (BR-03).',
      );
      expect(view.text, noWorkerNearbyText);
      expect(view.text, contains('trong bán kính 10 km'));
      expect(view.suggestRebook, isTrue);
    });

    test('a BR-10 rescue on a Premium order is not read as an agency gap', () {
      final view = cancelReasonView(
        'Cứu hộ khẩn cấp thất bại cho đơn Premium 12.',
      );
      expect(view.text, contains('người thay thế'));
    });

    test('a customer who refused a new worker is not told none was found', () {
      final view = cancelReasonView(
        'Khách hàng từ chối đổi thợ sau sự cố hiện trường (BR-10). Hoàn tiền 100%.',
      );
      expect(view.text, contains('Bạn đã chọn không đổi người làm'));
    });

    test('a payment that expired says no money was taken', () {
      expect(
        cancelReasonView('PAYMENT_EXPIRED').text,
        contains('không bị trừ tiền'),
      );
    });

    test("the customer's own reason is shown as typed, no rebook", () {
      final view = cancelReasonView('bận việc');
      expect(view.text, 'Lý do huỷ: bận việc');
      expect(view.suggestRebook, isFalse);
    });
  });
}

OrderSummary _order(
  int id,
  String date, {
  String shift = 'SHIFT_MORNING',
  String status = 'ASSIGNED',
}) => OrderSummary(
  orderId: id,
  orderCode: 'GV$id',
  serviceTier: 'ECONOMY',
  scheduledDate: date,
  shiftCode: shift,
  requiredWorkers: 1,
  totalAmount: 260000,
  orderStatus: status,
  createdAt: null,
);

const _shifts = BookingOptions(
  serviceTiers: ['ECONOMY', 'PREMIUM'],
  shifts: [
    ShiftOption(
      shiftCode: 'SHIFT_MORNING',
      startLocal: '08:00',
      endLocal: '12:00',
    ),
    ShiftOption(
      shiftCode: 'SHIFT_AFTERNOON',
      startLocal: '13:00',
      endLocal: '17:00',
    ),
  ],
  premiumMinLeadHours: 4,
  shiftMaxHours: 4,
  standardMaxAreaM2: 80,
  paymentQrExpiryMinutes: 15,
  sandbox: true,
);

String? _startOf(String shift) => switch (shift) {
  'SHIFT_MORNING' => '08:00',
  'SHIFT_AFTERNOON' => '13:00',
  _ => null,
};

void groupTests() {
  group('groupOrdersByMonth', () {
    test('one group per month even when the server sends them interleaved', () {
      // Newest created first: October, November, October again.
      final groups = groupOrdersByMonth([
        _order(3, '2026-10-12'),
        _order(2, '2026-11-02'),
        _order(1, '2026-10-05'),
      ], startOf: _startOf);

      expect(groups.map((g) => g.key), ['2026-11', '2026-10']);
      expect(groups[1].orders.map((o) => o.orderId), [3, 1]);
      expect(groups[1].year, 2026);
      expect(groups[1].month, 10);
    });

    test('inside a month the day, then the shift start, then the id run newest first', () {
      final groups = groupOrdersByMonth([
        _order(1, '2026-10-05'),
        _order(2, '2026-10-05', shift: 'SHIFT_AFTERNOON'),
        _order(3, '2026-10-09'),
        _order(4, '2026-10-05'),
      ], startOf: _startOf);

      expect(groups.single.orders.map((o) => o.orderId), [3, 2, 4, 1]);
    });

    test(
      'an order without a readable date goes to a last group with an empty key',
      () {
        final groups = groupOrdersByMonth([
          _order(1, ''),
          _order(2, '2026-10-05'),
          _order(3, '2025-12-31'),
        ]);

        expect(groups.map((g) => g.key), ['2026-10', '2025-12', '']);
      },
    );

    test('no orders, no groups', () {
      expect(groupOrdersByMonth(const []), isEmpty);
    });
  });

  group('orderStartsAt and upcomingOrders', () {
    test('the start is the local date plus the shift start', () {
      expect(
        orderStartsAt(
          _order(1, '2026-10-26', shift: 'SHIFT_AFTERNOON'),
          _shifts,
        ),
        DateTime.utc(2026, 10, 26, 13),
      );
      expect(
        orderStartsAt(_order(1, '2026-10-26', shift: 'SHIFT_X'), _shifts),
        isNull,
      );
    });

    test('vietnamNow adds seven hours', () {
      expect(
        vietnamNow(DateTime.utc(2026, 10, 25, 3)),
        DateTime.utc(2026, 10, 25, 10),
      );
    });

    test('only open orders that have not started, soonest first', () {
      final now = DateTime.utc(2026, 10, 26, 9);
      final result = upcomingOrders(
        [
          _order(1, '2026-10-26'), // 08:00 already started
          _order(2, '2026-10-27'),
          _order(3, '2026-10-26', shift: 'SHIFT_AFTERNOON'),
          _order(4, '2026-10-28', status: 'COMPLETED'),
          _order(5, '2026-10-28', status: 'CANCELLED'),
        ],
        _shifts,
        now,
      );

      expect(result.map((o) => o.orderId), [3, 2]);
    });
  });
}
