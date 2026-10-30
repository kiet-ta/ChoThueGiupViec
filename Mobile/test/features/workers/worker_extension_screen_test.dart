import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/network/api_response.dart';
import 'package:mobile/features/workers/models/worker_extension_models.dart';
import 'package:mobile/features/workers/routes.dart';
import 'package:mobile/features/workers/screens/worker_extension_screen.dart';
import 'package:mobile/features/workers/services/worker_extension_service.dart';

import 'design_frame.dart';

const offer = WorkerExtensionOffer(
  assignmentId: 2001,
  extensionId: 301,
  customerName: 'Nguyễn Văn An',
  workZone: 'Phòng khách & Bếp - Căn hộ 402',
  extraHours: 2,
  extraAmount: 130000,
  currentEndTime: '17:00',
  newEndTime: '19:00',
);

/// Answers like the server would, or fails; records what was asked.
class FakeExtensionService extends WorkerExtensionService {
  Object? failure;
  bool emptyAnswer = false;
  WorkerExtensionResponseDto? answer;
  final asked = <({int assignmentId, bool accept})>[];

  FakeExtensionService()
    : super(apiClient: ApiClient(baseUrl: 'http://api.test'));

  @override
  Future<ApiResponse<WorkerExtensionResponseDto>> respondExtension({
    required int assignmentId,
    required bool accept,
  }) async {
    asked.add((assignmentId: assignmentId, accept: accept));
    if (failure != null) throw failure!;
    return ApiResponse<WorkerExtensionResponseDto>(
      success: true,
      message: 'OK',
      data: emptyAnswer ? null : answer,
    );
  }
}

WorkerExtensionResponseDto answerOf(
  String status, {
  String end = '19:00',
  double hours = 2,
}) => WorkerExtensionResponseDto(
  assignmentId: 2001,
  extensionId: 301,
  extraHours: hours,
  extraAmount: 130000,
  status: status,
  newEndTime: end,
);

Future<void> pumpScreen(
  WidgetTester tester,
  FakeExtensionService service, {
  WorkerExtensionOffer shown = offer,
  void Function(WorkerExtensionResponseDto)? onResponded,
}) async {
  useDesignFrame(tester);
  await tester.pumpWidget(
    MaterialApp(
      home: WorkerExtensionScreen(
        offer: shown,
        service: service,
        onResponded: onResponded,
      ),
    ),
  );
  await tester.pumpAndSettle();
}

/// MOB-EXT-01 (#439): the screen shows the server's answer to the worker's accept / decline, never a made-up one.
void main() {
  group('WorkerExtensionScreen (MOB-M4-06, MOB-EXT-01)', () {
    testWidgets(
      'renders the offer: customer, zone, hours, amount and the two buttons',
      (tester) async {
        await pumpScreen(tester, FakeExtensionService());

        expect(find.text('Làm thêm giờ'), findsOneWidget);
        expect(find.text('Nguyễn Văn An'), findsOneWidget);
        expect(find.text('Phòng khách & Bếp - Căn hộ 402'), findsOneWidget);
        expect(find.text('2 giờ'), findsOneWidget);
        expect(find.text('17:00 ➔ 19:00'), findsOneWidget);
        expect(find.text('+130.000 đ'), findsOneWidget);
        expect(find.text('Đồng ý nối ca (+130.000 đ)'), findsOneWidget);
        expect(find.text('Từ chối làm thêm'), findsOneWidget);
      },
    );

    testWidgets('half hours are shown with a comma', (tester) async {
      await pumpScreen(
        tester,
        FakeExtensionService(),
        shown: const WorkerExtensionOffer(
          assignmentId: 2001,
          extensionId: 301,
          customerName: 'An',
          workZone: 'Bếp',
          extraHours: 1.5,
          extraAmount: 97500,
          currentEndTime: '12:00',
          newEndTime: '13:30',
        ),
      );

      expect(find.text('1,5 giờ'), findsOneWidget);
    });

    testWidgets(
      'accepting shows the server answer: status and the new end time',
      (tester) async {
        final service = FakeExtensionService()
          ..answer = answerOf('ACCEPTED', end: '13:30', hours: 1.5);
        WorkerExtensionResponseDto? seen;
        await pumpScreen(tester, service, onResponded: (r) => seen = r);

        await tapVisible(tester, find.text('Đồng ý nối ca (+130.000 đ)'));

        expect(service.asked.single, (assignmentId: 2001, accept: true));
        expect(find.text('Đã đồng ý nối ca'), findsOneWidget);
        expect(find.text('Đã đồng ý'), findsOneWidget);
        expect(find.text('13:30'), findsOneWidget);
        expect(find.textContaining('thêm 1,5h đến 13:30'), findsOneWidget);
        expect(seen?.status, 'ACCEPTED');
      },
    );

    testWidgets('declining shows the server answer: DECLINED', (tester) async {
      final service = FakeExtensionService()
        ..answer = answerOf('DECLINED', end: '17:00');
      WorkerExtensionResponseDto? seen;
      await pumpScreen(tester, service, onResponded: (r) => seen = r);

      await tapVisible(tester, find.text('Từ chối làm thêm'));

      expect(service.asked.single, (assignmentId: 2001, accept: false));
      expect(find.text('Đã từ chối yêu cầu'), findsOneWidget);
      expect(find.text('Đã từ chối'), findsOneWidget);
      expect(seen?.status, 'DECLINED');
    });

    for (final c in [
      (
        const ApiException(409, 'Invalid state'),
        'đã được trả lời hoặc chưa được thanh toán',
      ),
      (const ApiException(404, 'Not found'), 'Không tìm thấy yêu cầu gia hạn'),
      (const ApiException(403, 'Forbidden'), 'Không tìm thấy yêu cầu gia hạn'),
      (
        const ApiException(
          0,
          'Không kết nối được máy chủ. Vui lòng kiểm tra mạng và thử lại.',
        ),
        'Không kết nối được máy chủ',
      ),
      (const ApiException(500, 'Boom'), 'Boom'),
      (StateError('x'), 'Không gửi được câu trả lời'),
    ]) {
      testWidgets(
        'a failure (${c.$1.runtimeType} ${c.$1 is ApiException ? (c.$1 as ApiException).statusCode : ''}) is an error, not a made-up success',
        (tester) async {
          final service = FakeExtensionService()..failure = c.$1;
          WorkerExtensionResponseDto? seen;
          await pumpScreen(tester, service, onResponded: (r) => seen = r);

          await tapVisible(tester, find.text('Đồng ý nối ca (+130.000 đ)'));

          expect(find.textContaining(c.$2), findsOneWidget);
          expect(find.text('Đã đồng ý nối ca'), findsNothing);
          expect(find.text('Đã đồng ý'), findsNothing);
          expect(
            seen,
            isNull,
            reason: 'the caller is not told it was answered',
          );
          await scrollTo(tester, find.text('Từ chối làm thêm'));
          expect(
            find.text('Từ chối làm thêm'),
            findsOneWidget,
            reason: 'the buttons are usable again',
          );
        },
      );
    }

    testWidgets('an answer without data is an error, not a success', (
      tester,
    ) async {
      final service = FakeExtensionService()..emptyAnswer = true;
      await pumpScreen(tester, service);

      await tapVisible(tester, find.text('Từ chối làm thêm'));

      expect(find.textContaining('Máy chủ không trả kết quả'), findsOneWidget);
      expect(find.text('Đã từ chối yêu cầu'), findsNothing);
    });
  });

  group('WorkerExtensionResponseDto', () {
    Map<String, dynamic> json(Object? hours, Object? amount) => {
      'assignmentId': 2001,
      'extensionId': 301,
      'extraHours': hours,
      'extraAmount': amount,
      'status': 'ACCEPTED',
      'newEndTime': '13:30',
    };

    test('reads the decimals the backend sends (1.5 and 97500.0) as well as whole numbers', () {
      final decimal = WorkerExtensionResponseDto.fromJson(json(1.5, 97500.0));
      final whole = WorkerExtensionResponseDto.fromJson(json(2, 130000));

      expect(decimal.extraHours, 1.5);
      expect(decimal.extraAmount, 97500);
      expect(whole.extraHours, 2.0);
      expect(whole.extraAmount, 130000);
    });

    test('a missing or non-numeric value is 0', () {
      final dto = WorkerExtensionResponseDto.fromJson(json(null, 'x'));

      expect(dto.extraHours, 0.0);
      expect(dto.extraAmount, 0);
    });
  });

  group('the extension route', () {
    testWidgets(
      'without an offer it says there is nothing to answer (no sample offer)',
      (tester) async {
        useDesignFrame(tester);
        await tester.pumpWidget(
          MaterialApp(
            routes: WorkersFeatureModule().routes,
            initialRoute: WorkerRoutes.extension,
          ),
        );
        await tester.pumpAndSettle();

        expect(
          find.text('Chưa có yêu cầu gia hạn nào để trả lời.'),
          findsOneWidget,
        );
        expect(find.text('Nguyễn Văn An'), findsNothing);
        expect(find.textContaining('2001'), findsNothing);
      },
    );

    testWidgets('with an offer as argument it opens that offer', (
      tester,
    ) async {
      useDesignFrame(tester);
      await tester.pumpWidget(
        MaterialApp(
          routes: WorkersFeatureModule().routes,
          home: Builder(
            builder: (context) => TextButton(
              onPressed: () =>
                  Navigator.of(context)
                      .pushNamed(WorkerRoutes.extension, arguments: offer),
              child: const Text('open'),
            ),
          ),
        ),
      );
      await tester.tap(find.text('open'));
      await tester.pumpAndSettle();

      expect(find.text('Làm thêm giờ'), findsOneWidget);
    });
  });
}
