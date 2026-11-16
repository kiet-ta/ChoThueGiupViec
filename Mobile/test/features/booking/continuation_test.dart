import 'package:flutter_test/flutter_test.dart';

import 'package:mobile/features/booking/logic/booking_logic.dart';
import 'package:mobile/features/booking/models/booking_models.dart';
import 'package:mobile/features/workers/models/worker_assignment_models.dart';

/// D36 (LARGE-1W-01, #900): the 2nd job of a large ECONOMY house (extra hours right after the shift), as the customer and the worker read it.
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
          'status': 'STANDBY',
          'shiftCode': 'SHIFT_MORNING',
          'windowStartAt': '2026-11-20T05:00:00Z',
          'windowEndAt': '2026-11-20T09:00:00Z',
          'openedAt': null,
        },
      });

      expect(progress.continuation?.status, OrderContinuation.standby);
      expect(
        progress.continuation?.windowStartAt,
        DateTime.utc(2026, 11, 20, 5),
      );
      expect(progress.continuation?.windowEndAt, DateTime.utc(2026, 11, 20, 9));
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

    test(
      'says extra hours after the shift and that nothing more is charged',
      () {
        final standby = OrderContinuation(
          status: OrderContinuation.standby,
          windowStartAt: DateTime.utc(2026, 11, 20, 5),
          windowEndAt: DateTime.utc(2026, 11, 20, 9),
        );
        final open = OrderContinuation(
          status: OrderContinuation.open,
          windowStartAt: DateTime.utc(2026, 11, 20, 5),
          windowEndAt: DateTime.utc(2026, 11, 20, 9),
        );
        const released = OrderContinuation(status: OrderContinuation.released);

        expect(continuationText(standby), contains('thêm giờ ngay sau ca'));
        expect(continuationText(standby), contains('tối đa 4 giờ'));
        expect(continuationText(standby), contains('Không tính thêm tiền'));
        expect(continuationText(open), contains('Đơn nối tiếp đã mở'));
        expect(continuationText(released), isNull);
        expect(
          continuationText(
            const OrderContinuation(status: OrderContinuation.standby),
          ),
          contains('thêm giờ ngay sau ca'),
          reason: 'no window known: no hours promised',
        );
      },
    );

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
      final standby = WorkerContinuationDto.fromJson({
        'status': 'STANDBY',
        'shiftCode': 'SHIFT_MORNING',
      });
      final open = WorkerContinuationDto.fromJson({
        'status': 'OPEN',
        'shiftCode': 'SHIFT_MORNING',
      });
      final released = WorkerContinuationDto.fromJson({
        'status': 'RELEASED',
        'shiftCode': 'SHIFT_MORNING',
      });

      expect(standby?.note, contains('thêm giờ ngay sau ca'));
      expect(standby?.note, contains('Tiền công không đổi'));
      expect(open?.note, contains('Đơn nối tiếp đã mở'));
      expect(released?.note, isNull);
      expect(WorkerContinuationDto.fromJson(null), isNull);
    });
  });
}
