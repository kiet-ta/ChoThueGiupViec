import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/network/api_response.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/workers/models/worker_assignment_models.dart';
import 'package:mobile/features/workers/models/worker_extension_models.dart';
import 'package:mobile/features/workers/screens/worker_extension_loader.dart';
import 'package:mobile/features/workers/services/workers_assignments_service.dart';

import 'design_frame.dart';

// WORKERS-ASSIGNMENT-READ (#442): GET /api/v1/workers/assignments/{id} (contract workers.md 2.3.4).

Map<String, dynamic> _json({Map<String, dynamic>? pending}) => {
  'assignmentId': 55,
  'orderId': 155,
  'orderCode': 'GV-55',
  'slotDate': '2026-11-05',
  'shiftCode': 'SHIFT_AFTERNOON',
  'startTime': '13:00',
  'endTime': '17:00',
  'status': 'ASSIGNED',
  'serviceTier': 'PREMIUM',
  'addressLine': '12 Nguyễn Huệ',
  'district': 'Quận 1',
  'city': 'TP. Hồ Chí Minh',
  'latitude': 10.774,
  'longitude': 106.703,
  'payoutAmount': 128000.0,
  'pendingExtension': pending,
};

WorkerAssignmentDetailDto _detail({Map<String, dynamic>? pending}) =>
    WorkerAssignmentDetailDto.fromJson(_json(pending: pending));

const _pending = {
  'extensionId': 12,
  'extraHours': 2.0,
  'extraAmount': 160000.0,
};

/// Answers `getAssignment` from a queue; counts the calls.
class _FakeAssignments extends WorkersAssignmentsService {
  _FakeAssignments(this.answers) : super(apiClient: ApiClient());

  final List<ApiResponse<WorkerAssignmentDetailDto>> answers;
  int calls = 0;

  @override
  Future<ApiResponse<WorkerAssignmentDetailDto>> getAssignment(
    int assignmentId,
  ) async {
    final index = calls < answers.length ? calls : answers.length - 1;
    calls++;
    return answers[index];
  }
}

void main() {
  group('WorkerAssignmentDetailDto', () {
    test(
      'reads the full address, the coordinates and the pending extension',
      () {
        final d = _detail(pending: _pending);

        expect(d.addressLine, '12 Nguyễn Huệ');
        expect(d.fullAddress, '12 Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh');
        expect(d.latitude, 10.774);
        expect(d.longitude, 106.703);
        expect(d.payoutAmount, 128000);
        expect(d.pendingExtension!.extensionId, 12);
        expect(d.pendingExtension!.extraHours, 2.0);
        expect(d.pendingExtension!.extraAmount, 160000);
      },
    );

    test('a job without a waiting extension has none', () {
      expect(_detail().pendingExtension, isNull);
    });

    test('empty parts are left out of the one-line address', () {
      final d = WorkerAssignmentDetailDto.fromJson({
        ..._json(),
        'district': '',
        'city': '  ',
      });
      expect(d.fullAddress, '12 Nguyễn Huệ');
    });
  });

  group('WorkerExtensionOffer.fromDetail', () {
    test('is null without a waiting extension', () {
      expect(WorkerExtensionOffer.fromDetail(_detail()), isNull);
    });

    test('takes the real extension, the address and the end of the shift plus the extra hours', () {
      final offer = WorkerExtensionOffer.fromDetail(
        _detail(pending: _pending),
      )!;

      expect(offer.assignmentId, 55);
      expect(offer.extensionId, 12);
      expect(offer.title, 'Đơn GV-55');
      expect(offer.workZone, '12 Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh');
      expect(offer.extraHours, 2.0);
      expect(offer.extraAmount, 160000);
      expect(offer.currentEndTime, '17:00');
      expect(offer.newEndTime, '19:00');
    });

    test('half hours count as minutes and the clock wraps past midnight', () {
      final half = WorkerAssignmentDetailDto.fromJson({
        ..._json(
          pending: {'extensionId': 1, 'extraHours': 1.5, 'extraAmount': 1.0},
        ),
        'endTime': '20:30',
      });
      expect(WorkerExtensionOffer.fromDetail(half)!.newEndTime, '22:00');

      final late = WorkerAssignmentDetailDto.fromJson({
        ..._json(
          pending: {'extensionId': 1, 'extraHours': 3.0, 'extraAmount': 1.0},
        ),
        'endTime': '22:30',
      });
      expect(WorkerExtensionOffer.fromDetail(late)!.newEndTime, '01:30');
    });
  });

  group('WorkerExtensionLoader', () {
    Widget host(_FakeAssignments service) => MaterialApp(
      theme: NordicTheme.lightTheme,
      home: WorkerExtensionLoader(
        assignmentId: 55,
        assignmentsService: service,
      ),
    );

    testWidgets('shows the extension the server holds for the job', (
      tester,
    ) async {
      useDesignFrame(tester);
      final service = _FakeAssignments([
        ApiResponse(
          success: true,
          message: '',
          data: _detail(pending: _pending),
        ),
      ]);

      await tester.pumpWidget(host(service));
      await tester.pumpAndSettle();

      expect(find.text('Đơn GV-55'), findsOneWidget);
      expect(
        find.text('12 Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh'),
        findsOneWidget,
      );
      expect(find.text('17:00 ➔ 19:00'), findsOneWidget);
    });

    testWidgets('a job without a waiting extension says so', (tester) async {
      useDesignFrame(tester);
      final service = _FakeAssignments([
        ApiResponse(success: true, message: '', data: _detail()),
      ]);

      await tester.pumpWidget(host(service));
      await tester.pumpAndSettle();

      expect(
        find.text('Chưa có yêu cầu gia hạn nào để trả lời cho ca này.'),
        findsOneWidget,
      );
    });

    testWidgets(
      'a failed read is an error with a retry, never a sample offer',
      (tester) async {
        useDesignFrame(tester);
        final service = _FakeAssignments([
          const ApiResponse(
            success: false,
            message: 'Job assignment 55 not found.',
          ),
          ApiResponse(
            success: true,
            message: '',
            data: _detail(pending: _pending),
          ),
        ]);

        await tester.pumpWidget(host(service));
        await tester.pumpAndSettle();
        expect(find.text('Job assignment 55 not found.'), findsOneWidget);
        expect(find.textContaining('Đơn '), findsNothing);

        await tester.tap(find.text('Thử lại'));
        await tester.pumpAndSettle();

        expect(service.calls, 2);
        expect(find.text('Đơn GV-55'), findsOneWidget);
      },
    );
  });
}
