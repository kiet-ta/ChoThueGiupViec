import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/device/location_wrapper.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/network/api_response.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/dispatch/models/worker_cancel.dart';
import 'package:mobile/features/dispatch/screens/check_in_screen.dart';
import 'package:mobile/features/dispatch/services/dispatch_service.dart';
import 'package:mobile/features/dispatch/widgets/worker_cancel_section.dart';
import 'package:mobile/features/workers/models/worker_assignment_models.dart';
import 'package:mobile/features/workers/services/workers_assignments_service.dart';

/// DISPATCH-CANCEL-01 (#902, decisions D35): the worker's "Huỷ ca" on the arrival screen of an accepted job.

class _FakeAssignments extends WorkersAssignmentsService {
  _FakeAssignments(this.status) : super(apiClient: ApiClient());

  final String status;

  @override
  Future<ApiResponse<WorkerAssignmentDetailDto>> getAssignment(
    int assignmentId,
  ) async => ApiResponse(
    success: true,
    message: '',
    data: WorkerAssignmentDetailDto(
      assignmentId: assignmentId,
      orderId: 6,
      orderCode: 'GV-6',
      slotDate: '2026-11-20',
      shiftCode: 'SHIFT_MORNING',
      startTime: '08:00',
      endTime: '12:00',
      status: status,
      serviceTier: 'ECONOMY',
      addressLine: '12 Nguyễn Huệ',
      district: 'Quận 1',
      city: 'TP. Hồ Chí Minh',
      latitude: 10.774,
      longitude: 106.703,
      payoutAmount: 128000,
    ),
  );
}

/// Answers the preview and the cancel from fixed values; records the reason sent.
class _FakeDispatch extends DispatchService {
  _FakeDispatch({required this.preview, this.result, this.cancelError})
    : super(
        isMock: true,
        locationWrapper: LocationWrapper(
          mockLocation: GeoPoint(
            latitude: 10.774,
            longitude: 106.703,
            timestamp: DateTime(2026, 11, 19),
          ),
        ),
      );

  final WorkerCancelPreview preview;
  final WorkerCancelResult? result;
  final ApiException? cancelError;
  final List<String> reasons = [];

  @override
  Future<WorkerCancelPreview> getCancelPreview(int assignmentId) async =>
      preview;

  @override
  Future<WorkerCancelResult> cancelAssignment(
    int assignmentId,
    String reason,
  ) async {
    reasons.add(reason);
    if (cancelError != null) throw cancelError!;
    return result!;
  }
}

WorkerCancelPreview _preview({
  int count = 0,
  bool willLock = false,
  bool autoAccept = false,
  bool search = true,
  bool rescue = false,
}) => WorkerCancelPreview(
  assignmentId: 1001,
  cancelCountInWindow: count,
  lockCount: 3,
  windowDays: 30,
  willLockAccount: willLock,
  autoAcceptOn: autoAccept,
  jobStartAt: DateTime.utc(2026, 11, 20, 1),
  substituteSearchPossible: search,
  rescueFreeCancel: rescue,
);

WorkerCancelResult _result({
  int count = 1,
  bool locked = false,
  String outcome = WorkerCancelResult.redispatching,
  String status = 'CANCELLED_BY_WORKER',
}) => WorkerCancelResult(
  assignmentId: 1001,
  status: status,
  cancelCountInWindow: count,
  lockCount: 3,
  windowDays: 30,
  accountLocked: locked,
  orderOutcome: outcome,
);

Future<void> _pump(
  WidgetTester tester,
  _FakeDispatch dispatch, {
  String status = 'ASSIGNED',
}) async {
  tester.view.physicalSize = const Size(390, 1600);
  tester.view.devicePixelRatio = 1.0;
  addTearDown(() {
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
  await tester.pumpWidget(
    MaterialApp(
      theme: NordicTheme.lightTheme,
      home: CheckInScreen(
        assignmentId: 1001,
        dispatchService: dispatch,
        assignmentsService: _FakeAssignments(status),
      ),
    ),
  );
  await tester.pumpAndSettle();
}

Future<void> _tap(WidgetTester tester, String key) async {
  final finder = find.byKey(Key(key));
  await tester.ensureVisible(finder);
  await tester.tap(finder);
  await tester.pumpAndSettle();
}

void main() {
  testWidgets(
    'an accepted job shows the consequences first, needs a reason, then says a substitute is searched for',
    (tester) async {
      final dispatch = _FakeDispatch(
        preview: _preview(count: 1, autoAccept: true),
        result: _result(count: 2),
      );
      await _pump(tester, dispatch);

      await _tap(tester, 'open-worker-cancel');
      expect(
        find.text('Hệ thống sẽ báo gia chủ và tìm thợ khác thay bạn.'),
        findsOneWidget,
      );
      expect(
        find.text(
          'Đây là lần huỷ thứ 2 trong 30 ngày. Huỷ 3 lần, tài khoản sẽ bị khoá.',
        ),
        findsOneWidget,
      );
      expect(find.text('"Tự động nhận đơn" sẽ tắt.'), findsOneWidget);
      expect(find.byKey(const Key('worker-cancel-lock-warning')), findsNothing);
      expect(
        find.byKey(const Key('worker-cancel-report-incident')),
        findsOneWidget,
      );

      await _tap(tester, 'submit-worker-cancel');
      expect(
        find.text('Bạn cho gia chủ biết lý do huỷ ca nhé.'),
        findsOneWidget,
      );
      expect(dispatch.reasons, isEmpty);

      await tester.enterText(
        find.byKey(const Key('worker-cancel-reason')),
        '  Ốm đột xuất  ',
      );
      await _tap(tester, 'submit-worker-cancel');

      expect(dispatch.reasons, ['Ốm đột xuất']);
      expect(find.byKey(const Key('worker-cancelled-title')), findsOneWidget);
      expect(
        find.text('Hệ thống đã báo gia chủ và đang tìm thợ khác thay bạn.'),
        findsOneWidget,
      );
      expect(find.text('Lần huỷ thứ 2/3 trong 30 ngày'), findsOneWidget);
      expect(find.byKey(const Key('worker-cancel-done')), findsOneWidget);
      expect(find.byKey(const Key('check_in_gps_button')), findsNothing);
    },
  );

  testWidgets(
    'the third cancel warns about the lock, and a cancel close to the start says the order is refunded',
    (tester) async {
      final dispatch = _FakeDispatch(
        preview: _preview(count: 2, willLock: true, search: false),
        result: _result(
          count: 3,
          locked: true,
          outcome: WorkerCancelResult.cancelledRefunded,
        ),
      );
      await _pump(tester, dispatch);

      await _tap(tester, 'open-worker-cancel');
      expect(
        find.byKey(const Key('worker-cancel-lock-warning')),
        findsOneWidget,
      );
      expect(
        find.text(
          'Ca bắt đầu sau chưa tới 30 phút: đơn sẽ bị huỷ và gia chủ được hoàn tiền.',
        ),
        findsOneWidget,
      );

      await tester.enterText(
        find.byKey(const Key('worker-cancel-reason')),
        'Việc gia đình',
      );
      await _tap(tester, 'submit-worker-cancel');

      expect(
        find.text(
          'Ca bắt đầu quá gần nên đơn đã được huỷ và gia chủ được hoàn tiền.',
        ),
        findsOneWidget,
      );
      expect(find.text('Tài khoản đã bị khoá'), findsOneWidget);
    },
  );

  testWidgets(
    'a refusal of the server stays on the form with a plain message',
    (tester) async {
      final dispatch = _FakeDispatch(
        preview: _preview(),
        cancelError: const ApiException(409, 'ASSIGNMENT_STATE_INVALID'),
      );
      await _pump(tester, dispatch);

      await _tap(tester, 'open-worker-cancel');
      await tester.enterText(
        find.byKey(const Key('worker-cancel-reason')),
        'Bận',
      );
      await _tap(tester, 'submit-worker-cancel');

      expect(
        find.text(
          'Ca này không huỷ được nữa: đã tới giờ làm hoặc đã check-in.',
        ),
        findsOneWidget,
      );
      expect(find.byKey(const Key('worker-cancelled-title')), findsNothing);
    },
  );

  testWidgets(
    'a rescue seat (D38) says there is no penalty before and after, and the order is cancelled and refunded',
    (tester) async {
      final dispatch = _FakeDispatch(
        preview: _preview(rescue: true, search: false),
        result: _result(
          count: 0,
          status: 'CANCELLED',
          outcome: WorkerCancelResult.cancelledRefunded,
        ),
      );
      await _pump(tester, dispatch);

      await _tap(tester, 'open-worker-cancel');
      expect(
        find.byKey(const Key('worker-cancel-rescue-free')),
        findsOneWidget,
      );
      expect(
        find.text(
          'Đây là ca cứu hộ: huỷ trước giờ check-in thì không bị phạt và không tính vào số lần huỷ.',
        ),
        findsOneWidget,
      );
      expect(
        find.text('Đơn sẽ bị huỷ và gia chủ được hoàn tiền.'),
        findsOneWidget,
      );
      // None of the usual violation lines.
      expect(find.textContaining('lần huỷ thứ'), findsNothing);
      expect(find.textContaining('tài khoản sẽ bị khoá'), findsNothing);
      expect(find.textContaining('tìm thợ khác thay bạn'), findsNothing);

      await tester.enterText(
        find.byKey(const Key('worker-cancel-reason')),
        'Không kịp đến',
      );
      await _tap(tester, 'submit-worker-cancel');

      expect(find.byKey(const Key('worker-cancelled-title')), findsOneWidget);
      expect(
        find.text(
          'Bạn huỷ ca cứu hộ trước giờ check-in nên không bị phạt. Đơn đã được huỷ và gia chủ được hoàn tiền.',
        ),
        findsOneWidget,
      );
      expect(
        find.byKey(const Key('worker-cancelled-no-penalty')),
        findsOneWidget,
      );
      expect(find.textContaining('Lần huỷ thứ'), findsNothing);
    },
  );

  testWidgets(
    'an ordinary job keeps the usual consequences (no rescue notice)',
    (tester) async {
      await _pump(tester, _FakeDispatch(preview: _preview()));

      await _tap(tester, 'open-worker-cancel');

      expect(find.byKey(const Key('worker-cancel-rescue-free')), findsNothing);
      expect(
        find.text('Hệ thống sẽ báo gia chủ và tìm thợ khác thay bạn.'),
        findsOneWidget,
      );
    },
  );

  testWidgets('a job already checked in has no cancel button', (tester) async {
    await _pump(
      tester,
      _FakeDispatch(preview: _preview()),
      status: 'CHECKED_IN',
    );

    expect(find.byKey(const Key('open-worker-cancel')), findsNothing);
  });

  test('preview and result read the contract shapes', () {
    final p = WorkerCancelPreview.fromJson({
      'assignmentId': 1001,
      'cancelCountInWindow': 2,
      'lockCount': 3,
      'windowDays': 30,
      'willLockAccount': true,
      'autoAcceptOn': false,
      'jobStartAt': '2026-11-20T01:00:00Z',
      'substituteSearchPossible': false,
    });
    expect(
      (p.nextCount, p.willLockAccount, p.substituteSearchPossible),
      (3, true, false),
    );
    expect(
      p.rescueFreeCancel,
      isFalse,
    ); // the field is absent on an ordinary job
    expect(
      WorkerCancelPreview.fromJson({
        'assignmentId': 1,
        'rescueFreeCancel': true,
      }).rescueFreeCancel,
      isTrue,
    );
    expect(p.jobStartAt, DateTime.utc(2026, 11, 20, 1));

    final r = WorkerCancelResult.fromJson({
      'assignmentId': 1001,
      'status': 'CANCELLED_BY_WORKER',
      'cancelCountInWindow': 3,
      'lockCount': 3,
      'windowDays': 30,
      'accountLocked': true,
      'orderOutcome': 'CANCELLED_REFUNDED',
      'substituteSearchUntil': null,
    });
    expect(
      (r.accountLocked, r.orderOutcome),
      (true, WorkerCancelResult.cancelledRefunded),
    );
    expect(
      r.isFreeRescueCancel,
      isFalse,
    ); // CANCELLED_BY_WORKER counts as a violation
    expect(
      WorkerCancelResult.fromJson({
        'assignmentId': 1001,
        'status': 'CANCELLED',
        'orderOutcome': 'CANCELLED_REFUNDED',
      }).isFreeRescueCancel,
      isTrue,
    );
    expect(validateWorkerCancelReason('a' * 256), isNotNull);
    expect(validateWorkerCancelReason(' ok '), isNull);
    expect(
      workerCancelFailure(const ApiException(403, 'x')),
      contains('doanh nghiệp'),
    );
  });
}
