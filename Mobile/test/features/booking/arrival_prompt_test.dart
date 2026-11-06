import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/booking/models/booking_models.dart';
import 'package:mobile/features/booking/screens/arrival_prompt_card.dart';
import 'package:mobile/features/booking/services/order_service.dart';

class FakeOrderServiceForArrival implements IOrderService {
  final confirmed = <int>[];
  Object? confirmError;
  CustomerArrivalStatus? arrivalStatus;

  @override
  Future<CustomerArrivalStatus?> getArrivalStatus(int assignmentId) =>
      Future.value(arrivalStatus);

  @override
  Future<bool> confirmArrival(int assignmentId) {
    confirmed.add(assignmentId);
    if (confirmError != null) return Future.error(confirmError!);
    return Future.value(true);
  }

  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

Future<void> pumpArrivalCard(
  WidgetTester tester,
  FakeOrderServiceForArrival service, {
  int assignmentId = 11,
  String? workerName = 'Nghia',
  double? distanceMeters = 16546.0,
  VoidCallback? onConfirmed,
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
          child: ArrivalPromptCard(
            assignmentId: assignmentId,
            workerName: workerName,
            distanceMeters: distanceMeters,
            service: service,
            onConfirmed: onConfirmed,
          ),
        ),
      ),
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  group('ArrivalPromptCard (CUST-ARRIVAL-01)', () {
    testWidgets('displays worker name and formatted distance', (tester) async {
      final service = FakeOrderServiceForArrival();
      await pumpArrivalCard(tester, service);

      expect(find.text('Thợ báo đã đến nơi'), findsOneWidget);
      expect(
        find.textContaining(
          'Thợ Nghia báo đã có mặt nhưng GPS chưa khớp địa chỉ',
        ),
        findsOneWidget,
      );
      expect(find.textContaining('16546 m'), findsOneWidget);
      expect(find.byKey(const Key('arrival-btn-start-11')), findsOneWidget);
    });

    testWidgets(
      'tapping start confirmation reveals confirm and cancel buttons',
      (tester) async {
        final service = FakeOrderServiceForArrival();
        await pumpArrivalCard(tester, service);

        await tester.tap(find.byKey(const Key('arrival-btn-start-11')));
        await tester.pumpAndSettle();

        expect(
          find.text('Bạn xác nhận Thợ Nghia đang có mặt tại nhà?'),
          findsOneWidget,
        );
        expect(find.byKey(const Key('arrival-btn-confirm-11')), findsOneWidget);
        expect(find.byKey(const Key('arrival-btn-cancel-11')), findsOneWidget);

        // Cancel resets back to start
        await tester.tap(find.byKey(const Key('arrival-btn-cancel-11')));
        await tester.pumpAndSettle();

        expect(find.byKey(const Key('arrival-btn-start-11')), findsOneWidget);
      },
    );

    testWidgets('confirming invokes service and onConfirmed callback', (
      tester,
    ) async {
      final service = FakeOrderServiceForArrival();
      var confirmed = false;
      await pumpArrivalCard(
        tester,
        service,
        onConfirmed: () => confirmed = true,
      );

      await tester.tap(find.byKey(const Key('arrival-btn-start-11')));
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('arrival-btn-confirm-11')));
      await tester.pumpAndSettle();

      expect(service.confirmed, equals([11]));
      expect(confirmed, isTrue);
      expect(find.text('Đã xác nhận thợ có mặt tại nhà.'), findsOneWidget);
    });

    testWidgets('shows error message when confirm fails', (tester) async {
      final service = FakeOrderServiceForArrival()
        ..confirmError = const ApiException(409, 'Conflict');
      await pumpArrivalCard(tester, service);

      await tester.tap(find.byKey(const Key('arrival-btn-start-11')));
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('arrival-btn-confirm-11')));
      await tester.pumpAndSettle();

      expect(
        find.text('Thợ chưa báo đến nơi hoặc ca làm đã được xác nhận.'),
        findsOneWidget,
      );
    });
  });
}
