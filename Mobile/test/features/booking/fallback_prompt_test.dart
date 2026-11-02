import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/booking/logic/order_logic.dart';
import 'package:mobile/features/booking/models/booking_models.dart';
import 'package:mobile/features/booking/models/fallback_models.dart';
import 'package:mobile/features/booking/screens/fallback_prompt_card.dart';
import 'package:mobile/features/booking/services/fallback_service.dart';

class FakeFallbackService implements IFallbackService {
  FallbackStatus status = const FallbackStatus(
    orderId: 100,
    state: FallbackState.awaitingCustomer,
    workerId: 101,
    secondShiftCode: 'SHIFT_AFTERNOON',
  );
  Object? statusError;
  Object? respondError;
  FallbackResponse response = const FallbackResponse(
    orderId: 100,
    outcome: FallbackOutcome.workerAsked,
  );
  Completer<FallbackResponse>? gate;

  int statusReads = 0;
  final answers = <bool>[];

  @override
  Future<FallbackStatus> getStatus(int orderId) {
    statusReads++;
    return statusError != null
        ? Future.error(statusError!)
        : Future.value(status);
  }

  @override
  Future<FallbackResponse> respond(int orderId, {required bool accept}) {
    answers.add(accept);
    if (gate != null) return gate!.future;
    if (respondError != null) return Future.error(respondError!);
    return Future.value(response);
  }
}

Future<void> pumpCard(
  WidgetTester tester,
  FakeFallbackService service, {
  VoidCallback? onAnswered,
}) async {
  tester.view.physicalSize = const Size(390, 1200);
  tester.view.devicePixelRatio = 1.0;
  addTearDown(() {
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
  await tester.pumpWidget(
    MaterialApp(
      theme: NordicTheme.lightTheme,
      home: Scaffold(
        body: SingleChildScrollView(
          child: FallbackPromptCard(
            orderId: 100,
            service: service,
            onAnswered: onAnswered,
          ),
        ),
      ),
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  group('FallbackPromptCard (BR-02 fallback, customer side)', () {
    testWidgets('asks whether one worker may do two consecutive shifts', (
      tester,
    ) async {
      await pumpCard(tester, FakeFallbackService());

      expect(find.byKey(const Key('fallback-title')), findsOneWidget);
      expect(find.byKey(const Key('fallback-accept')), findsOneWidget);
      expect(find.byKey(const Key('fallback-decline')), findsOneWidget);
    });

    testWidgets('names the second shift the server reports and the deadline', (
      tester,
    ) async {
      final service = FakeFallbackService()
        ..status = FallbackStatus(
          orderId: 100,
          state: FallbackState.awaitingCustomer,
          workerId: 101,
          secondShiftCode: 'SHIFT_EVENING',
          deadline: DateTime.utc(2026, 10, 15, 1),
        );
      await pumpCard(tester, service);

      final question = tester
          .widget<Text>(find.byKey(const Key('fallback-question')))
          .data!;
      expect(question, contains('ca tối'));
      expect(question, isNot(contains('ca sáng')));
      expect(find.byKey(const Key('fallback-deadline')), findsOneWidget);
    });

    testWidgets('without a second shift code the question stays neutral', (
      tester,
    ) async {
      final service = FakeFallbackService()
        ..status = const FallbackStatus(
          orderId: 100,
          state: FallbackState.awaitingCustomer,
        );
      await pumpCard(tester, service);

      expect(
        tester.widget<Text>(find.byKey(const Key('fallback-question'))).data,
        contains('ca liền sau'),
      );
      expect(find.byKey(const Key('fallback-deadline')), findsNothing);
    });

    testWidgets('agreeing sends accept=true and waits for the worker', (
      tester,
    ) async {
      final service = FakeFallbackService();
      var answered = 0;
      await pumpCard(tester, service, onAnswered: () => answered++);

      await tester.tap(find.byKey(const Key('fallback-accept')));
      await tester.pumpAndSettle();

      expect(service.answers, [true]);
      expect(answered, 1);
      expect(find.byKey(const Key('fallback-waiting-worker')), findsOneWidget);
      expect(find.byKey(const Key('fallback-accept')), findsNothing);
    });

    testWidgets('declining sends accept=false and says it was refunded', (
      tester,
    ) async {
      final service = FakeFallbackService()
        ..response = const FallbackResponse(
          orderId: 100,
          outcome: FallbackOutcome.cancelled,
        );
      await pumpCard(tester, service);

      await tester.tap(find.byKey(const Key('fallback-decline')));
      await tester.pumpAndSettle();

      expect(service.answers, [false]);
      expect(
        tester.widget<Text>(find.byKey(const Key('fallback-notice'))).data,
        contains('hoàn tiền 100 %'),
      );
    });

    testWidgets('a double tap sends one request', (tester) async {
      final service = FakeFallbackService()..gate = Completer();
      await pumpCard(tester, service);

      await tester.tap(find.byKey(const Key('fallback-accept')));
      await tester.pump();
      await tester.tap(
        find.byKey(const Key('fallback-decline')),
        warnIfMissed: false,
      );
      await tester.pump();

      expect(service.answers, [true]);
      service.gate!.complete(service.response);
      await tester.pumpAndSettle();
    });

    testWidgets('a stale answer (409) reloads instead of showing a dead end', (
      tester,
    ) async {
      final service = FakeFallbackService()
        ..respondError = const ApiException(
          409,
          'Đơn không còn chờ.',
          code: FallbackErrorCode.notWaiting,
        );
      var answered = 0;
      await pumpCard(tester, service, onAnswered: () => answered++);

      await tester.tap(find.byKey(const Key('fallback-accept')));
      await tester.pumpAndSettle();

      expect(answered, 1);
      expect(service.statusReads, 2);
    });

    testWidgets('a failed load shows the reason and retries', (tester) async {
      final service = FakeFallbackService()
        ..statusError = const ApiException(0, 'Không kết nối được máy chủ.');
      await pumpCard(tester, service);

      expect(find.text('Không kết nối được máy chủ.'), findsOneWidget);

      service.statusError = null;
      await tester.tap(find.byKey(const Key('fallback-retry')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('fallback-accept')), findsOneWidget);
    });

    testWidgets('an order that is not waiting shows nothing', (tester) async {
      final service = FakeFallbackService()
        ..status = const FallbackStatus(
          orderId: 100,
          state: FallbackState.notWaiting,
        );
      await pumpCard(tester, service);

      expect(find.byKey(const Key('fallback-card')), findsNothing);
    });
  });

  group('order status AWAITING_FALLBACK', () {
    test('has a label and sits on the "find a worker" step', () {
      expect(
        orderStatusLabel(OrderStatus.awaitingFallback),
        'Chờ bạn chọn: 1 người làm 2 ca',
      );
      final steps = orderTimeline(OrderStatus.awaitingFallback);
      expect(steps[1].current, isTrue);
      expect(shouldPollOrder(OrderStatus.awaitingFallback), isTrue);
      expect(canCancel(OrderStatus.awaitingFallback), isTrue);
    });
  });
}
