import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/booking/models/booking_models.dart';
import 'package:mobile/features/workers/logic/acceptance_logic.dart';
import 'package:mobile/features/workers/models/worker_assignment_models.dart';

/// ACCEPT-02 (decision D32): the acceptance phases and deadlines as both sides read them.
void main() {
  final t = DateTime.utc(2026, 11, 14, 3); // 10:00 Vietnam time
  AcceptanceBlock block(String phase, {String? source}) => AcceptanceBlock(
    phase: phase,
    submittedAt: t,
    redoUntil: t.add(const Duration(minutes: 30)),
    autoAcceptAt:
        phase == AcceptancePhase.onSite || phase == AcceptancePhase.review
        ? t.add(const Duration(hours: 24))
        : null,
    disputeUntil: t.add(const Duration(hours: 24)),
    completionSource: source,
  );

  group('reading the block', () {
    test('every field of the server block, and null for no block', () {
      final b = AcceptanceBlock.fromJson({
        'phase': 'REVIEW',
        'submittedAt': '2026-11-14T03:00:00Z',
        'redoUntil': '2026-11-14T03:30:00Z',
        'autoAcceptAt': '2026-11-15T03:00:00Z',
        'disputeUntil': '2026-11-15T03:00:00Z',
        'completionSource': null,
        'redoCount': 2,
      })!;
      expect(b.phase, 'REVIEW');
      expect(b.autoAcceptAt, DateTime.utc(2026, 11, 15, 3));
      expect(b.redoCount, 2);
      expect(AcceptanceBlock.fromJson(null), isNull);
    });

    test('both models carry it', () {
      final progress = ProgressAssignment.fromJson({
        'assignmentId': 1,
        'assignmentStatus': 'AWAITING_ACCEPTANCE',
        'acceptance': {'phase': 'ON_SITE'},
      });
      expect(progress.acceptance!.phase, AcceptancePhase.onSite);
      final detail = WorkerAssignmentDetailDto.fromJson({
        'assignmentId': 1,
        'status': 'COMPLETED',
        'acceptance': {'phase': 'COMPLETED', 'completionSource': 'AUTO'},
      });
      expect(detail.acceptance!.completionSource, CompletionSourceValue.auto);
    });
  });

  group('times', () {
    test('HH:mm in Vietnam time, with the day when it is not today', () {
      expect(acceptanceTime(t.add(const Duration(minutes: 30)), t), '10:30');
      expect(
        acceptanceTime(t.add(const Duration(hours: 24)), t),
        '10:00 15/11',
      );
    });
  });

  group('customer', () {
    test('each phase has its title and sentence', () {
      expect(
        customerAcceptanceView(block(AcceptancePhase.onSite), t)!.title,
        'Mời bạn kiểm tra',
      );
      expect(
        customerAcceptanceView(block(AcceptancePhase.onSite), t)!.note,
        contains('Người làm còn ở đây đến 10:30'),
      );
      expect(
        customerAcceptanceView(block(AcceptancePhase.review), t)!.note,
        'Tự nghiệm thu lúc 10:00 15/11 nếu bạn không phản hồi.',
      );
      expect(
        customerAcceptanceView(block(AcceptancePhase.disputed), t)!.title,
        'Đang xử lý khiếu nại',
      );
      expect(
        customerAcceptanceView(
          block(AcceptancePhase.completed, source: 'CUSTOMER'),
          t,
        )!.note,
        'Bạn đã nghiệm thu ca này.',
      );
      expect(
        customerAcceptanceView(
          block(AcceptancePhase.completed, source: 'AUTO'),
          t,
        )!.note,
        'Đã tự nghiệm thu sau 24 giờ.',
      );
      expect(
        customerAcceptanceView(
          block(AcceptancePhase.completed, source: 'VERDICT'),
          t,
        )!.note,
        'Đã đóng theo kết quả khiếu nại.',
      );
      expect(customerAcceptanceView(null, t), isNull);
    });

    test('an unknown phase says it is updating', () {
      expect(
        customerAcceptanceView(const AcceptanceBlock(phase: 'LATER'), t)!.title,
        unknownAcceptanceTitle,
      );
      expect(
        workerAcceptanceView(const AcceptanceBlock(phase: 'LATER'), t)!.title,
        unknownAcceptanceTitle,
      );
    });

    test('a redo only before redoUntil, never once completed', () {
      expect(
        canAskRedo(
          block(AcceptancePhase.onSite),
          t.add(const Duration(minutes: 29, seconds: 59)),
        ),
        isTrue,
      );
      expect(
        canAskRedo(
          block(AcceptancePhase.onSite),
          t.add(const Duration(minutes: 30)),
        ),
        isFalse,
      );
      expect(canAskRedo(block(AcceptancePhase.completed), t), isFalse);
      expect(
        canAskRedo(null, t),
        isTrue,
      ); // an older server: the server decides
    });
  });

  group('worker', () {
    test('each phase has its title and sentence', () {
      expect(
        workerAcceptanceView(block(AcceptancePhase.onSite), t)!.title,
        'Khách đang kiểm tra',
      );
      expect(
        workerAcceptanceView(block(AcceptancePhase.onSite), t)!.note,
        contains('dọn lại đến 10:30'),
      );
      expect(
        workerAcceptanceView(block(AcceptancePhase.review), t)!.title,
        'Đã bàn giao',
      );
      expect(
        workerAcceptanceView(block(AcceptancePhase.review), t)!.note,
        contains('chậm nhất 10:00 15/11'),
      );
      expect(
        workerAcceptanceView(block(AcceptancePhase.disputed), t)!.title,
        'Ca đang có khiếu nại',
      );
      expect(
        workerAcceptanceView(
          block(AcceptancePhase.completed, source: 'CUSTOMER'),
          t,
        )!.note,
        'Khách đã nghiệm thu.',
      );
      expect(
        workerAcceptanceView(
          block(AcceptancePhase.completed, source: 'VERDICT'),
          t,
        )!.note,
        'Đã đóng theo kết quả khiếu nại.',
      );
    });
  });
}
