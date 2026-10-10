import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/booking/logic/order_logic.dart';
import 'package:mobile/features/booking/models/booking_models.dart';

ProgressAssignment _assignment(int id, String status, {String? name = 'Lan', double rating = 4.8}) => ProgressAssignment(
      assignmentId: id,
      assignmentSeq: 1,
      workerId: id + 100,
      workerName: name,
      workerRatingAvg: rating,
      assignmentStatus: status,
      acceptedAt: null,
      completedAt: null,
    );

OrderProgress _progress(String orderStatus, List<ProgressAssignment> assignments, {OrderExtension? extension}) =>
    OrderProgress(orderId: 1, orderStatus: orderStatus, requiredWorkers: assignments.length, assignments: assignments, extension: extension);

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
  group('labels', () {
    test('every order status of the contract has a Vietnamese label; an unknown one is shown as is', () {
      for (final s in ['PENDING_PAYMENT', 'PAID', 'DISPATCHING', 'ASSIGNED', 'COMPLETED', 'CANCELLED']) {
        expect(orderStatusLabel(s), isNot(s));
      }
      expect(orderStatusLabel('NEW_STATUS'), 'NEW_STATUS');
    });

    test('every visible assignment status and extension status has a label', () {
      for (final s in ['ASSIGNED', 'CHECKED_IN', 'IN_PROGRESS', 'AWAITING_ACCEPTANCE', 'COMPLETED', 'CANCELLED_BY_WORKER', 'ABSENT', 'INCIDENT']) {
        expect(assignmentStatusLabel(s), isNot(s));
      }
      for (final s in ['PENDING_PAYMENT', 'PAID', 'ACCEPTED', 'DECLINED', 'EXPIRED']) {
        expect(extensionStatusLabel(s), isNot(s));
      }
    });

    test('worker name falls back, rating and hours use the Vietnamese comma', () {
      expect(workerName(_assignment(1, 'ASSIGNED', name: null)), 'Thợ');
      expect(workerName(_assignment(1, 'ASSIGNED', name: '  ')), 'Thợ');
      expect(workerName(_assignment(1, 'ASSIGNED')), 'Lan');
      expect(ratingText(4.8), '4,8');
      expect(ratingText(0), '');
      expect(hoursText(1.5), '1,5 giờ');
      expect(hoursText(2), '2 giờ');
    });

    test('schedule text turns the contract date around', () {
      expect(scheduleText('2026-10-15', 'SHIFT_MORNING'), '15/10/2026 · Ca sáng');
      expect(scheduleText('bad', 'SHIFT_EVENING'), 'bad · Ca tối');
    });
  });

  group('orderTimeline', () {
    List<String> current(String status) => [for (final s in orderTimeline(status)) if (s.current) s.label];
    List<String> done(String status) => [for (final s in orderTimeline(status)) if (s.done) s.label];

    test('walks pay, find, assigned, done', () {
      expect(current('PENDING_PAYMENT'), ['Thanh toán']);
      expect(done('PENDING_PAYMENT'), isEmpty);
      expect(current('DISPATCHING'), ['Tìm thợ']);
      expect(done('DISPATCHING'), ['Thanh toán']);
      expect(current('ASSIGNED'), ['Có thợ']);
      expect(done('ASSIGNED'), ['Thanh toán', 'Tìm thợ']);
    });

    test('PAID is shown as finding a worker', () {
      expect(current('PAID'), ['Tìm thợ']);
    });

    test('a completed order has every step done and none current', () {
      expect(done('COMPLETED').length, 4);
      expect(current('COMPLETED'), isEmpty);
    });

    test('a cancelled order is one step', () {
      expect(orderTimeline('CANCELLED').single.label, 'Đã huỷ');
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
      expect(canExtend(_progress('ASSIGNED', [_assignment(1, 'IN_PROGRESS')])), isTrue);
      expect(canExtend(_progress('ASSIGNED', [_assignment(1, 'AWAITING_ACCEPTANCE')])), isTrue);
      expect(canExtend(_progress('ASSIGNED', [_assignment(1, 'CHECKED_IN')])), isFalse);
      expect(canExtend(_progress('ASSIGNED', [_assignment(1, 'ASSIGNED')])), isFalse);
      expect(canExtend(_progress('COMPLETED', [_assignment(1, 'IN_PROGRESS')])), isFalse);
      expect(canExtend(_progress('ASSIGNED', [_assignment(1, 'IN_PROGRESS')], extension: _extension)), isFalse);
    });

    test('only on-site workers can be asked', () {
      final p = _progress('ASSIGNED', [_assignment(1, 'IN_PROGRESS'), _assignment(2, 'ABSENT'), _assignment(3, 'AWAITING_ACCEPTANCE')]);
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
      expect(cancelFailure(const ApiException(409, 'x', code: 'INVALID_STATE')), contains('không huỷ được'));
      expect(
        cancelFailure(const ApiException(400, 'Validation failed', fieldErrors: {'reason': ['reason is required.']})),
        'reason is required.',
      );
      expect(cancelFailure(const ApiException(404, 'x')), contains('Không tìm thấy'));
    });

    test('a refused extension is explained by its code, never by the server message', () {
      expect(extensionFailure(const ApiException(409, 'server text', code: 'EXTENSION_EXISTS')), contains('đã có'));
      expect(extensionFailure(const ApiException(409, 'server text', code: 'INVALID_STATE')), contains('đang làm việc'));
      expect(extensionFailure(const ApiException(409, 'server text')), isNot(contains('server text')));
      expect(
        extensionFailure(const ApiException(400, 'Validation failed', fieldErrors: {'extraHours': ['extraHours must be a multiple of 0.5.']})),
        contains('0.5'),
      );
    });

    test('network, session and role problems read the same everywhere', () {
      expect(orderLoadFailure(const ApiException(0, 'Không kết nối được máy chủ.')), 'Không kết nối được máy chủ.');
      expect(orderLoadFailure(const ApiException(401, 'x')), contains('đăng nhập'));
      expect(orderLoadFailure(const ApiException(403, 'x')), contains('khách hàng'));
      expect(orderLoadFailure(const ApiException(500, 'x')), contains('Máy chủ gặp lỗi'));
    });
  });
}
