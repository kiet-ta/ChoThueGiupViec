import 'package:flutter_test/flutter_test.dart';

import 'package:mobile/features/booking/logic/booking_logic.dart';
import 'package:mobile/features/booking/models/booking_models.dart';
import 'package:mobile/features/workers/models/worker_assignment_models.dart';

/// D35 (LARGE-1W-01, #900): the 2nd job of a large ECONOMY house, as the customer and the worker read it.
void main() {
  group('customer', () {
    test('reads the continuation of the order progress', () {
      final progress = OrderProgress.fromJson({
        'orderId': 9,
        'orderStatus': 'ASSIGNED',
        'requiredWorkers': 1,
        'assignments': [],
        'extension': null,
        'continuation': {
          'status': 'RESERVED',
          'shiftCode': 'SHIFT_AFTERNOON',
          'windowStartAt': '2026-11-20T06:00:00Z',
          'windowEndAt': '2026-11-20T10:00:00Z',
          'openedAt': null,
        },
      });

      expect(progress.continuation?.status, OrderContinuation.reserved);
      expect(progress.continuation?.shiftCode, 'SHIFT_AFTERNOON');
      expect(
        progress.continuation?.windowStartAt,
        DateTime.utc(2026, 11, 20, 6),
      );
    });

    test('an ordinary order has no continuation', () {
      final progress = OrderProgress.fromJson({
        'orderId': 9,
        'orderStatus': 'ASSIGNED',
        'requiredWorkers': 1,
        'assignments': [],
      });

      expect(progress.continuation, isNull);
      expect(continuationText(progress.continuation), isNull);
    });

    test('says the next shift and that nothing more is charged', () {
      const reserved = OrderContinuation(
        status: OrderContinuation.reserved,
        shiftCode: 'SHIFT_AFTERNOON',
      );
      const open = OrderContinuation(
        status: OrderContinuation.open,
        shiftCode: 'SHIFT_AFTERNOON',
      );
      const released = OrderContinuation(
        status: OrderContinuation.released,
        shiftCode: 'SHIFT_AFTERNOON',
      );

      expect(continuationText(reserved), contains('ca chiều'));
      expect(continuationText(reserved), contains('Không tính thêm tiền'));
      expect(continuationText(open), contains('Đơn nối tiếp đã mở'));
      expect(continuationText(released), isNull);
      expect(
        continuationText(
          const OrderContinuation(status: OrderContinuation.reserved),
        ),
        isNull,
        reason: 'no next shift, nothing to show',
      );
    });

    test('the order carries the large-house flag', () {
      final order = BookingOrder.fromJson({
        'orderId': 1,
        'requiredWorkers': 1,
        'totalAmount': 520000,
        'isContinuousWork': true,
      });

      expect(order.isContinuousWork, isTrue);
      expect(BookingOrder.fromJson({'orderId': 1}).isContinuousWork, isFalse);
    });
  });

  group('worker', () {
    test('reads and words the 2nd job', () {
      final reserved = WorkerContinuationDto.fromJson({
        'status': 'RESERVED',
        'shiftCode': 'SHIFT_AFTERNOON',
      });
      final open = WorkerContinuationDto.fromJson({
        'status': 'OPEN',
        'shiftCode': 'SHIFT_AFTERNOON',
      });
      final released = WorkerContinuationDto.fromJson({
        'status': 'RELEASED',
        'shiftCode': 'SHIFT_AFTERNOON',
      });

      expect(reserved?.note, contains('ca chiều'));
      expect(reserved?.note, contains('Tiền công không đổi'));
      expect(open?.note, contains('Đơn nối tiếp đã mở'));
      expect(released?.note, isNull);
      expect(WorkerContinuationDto.fromJson(null), isNull);
    });
  });
}
