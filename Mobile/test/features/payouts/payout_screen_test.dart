import 'dart:async';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/payouts/models/payout_models.dart';
import 'package:mobile/features/payouts/screens/payout_history_screen.dart';
import 'package:mobile/features/payouts/screens/payout_screen.dart';
import 'package:mobile/features/payouts/services/payouts_service.dart';

Earnings earnings(String month, {int net = 430000, String status = 'NOT_BUILT', List<EarningsJob>? jobs}) => Earnings(
      periodMonth: month,
      jobCount: (jobs ?? _jobs).length,
      grossAmount: 600000,
      commissionAmount: 120000,
      penaltyAmount: 50000,
      netAmount: net,
      payoutStatus: status,
      jobs: jobs ?? _jobs,
    );

final _jobs = [
  EarningsJob(assignmentId: 1, orderId: 10, completedAt: DateTime.utc(2026, 9, 30, 17, 30), grossAmount: 400000, commissionAmount: 80000, netAmount: 320000, absenceFee: false),
  EarningsJob(assignmentId: 2, orderId: 11, completedAt: DateTime.utc(2026, 10, 2, 3), grossAmount: 200000, commissionAmount: 0, netAmount: 200000, absenceFee: true),
];

class FakePayoutsService implements IPayoutsService {
  final earningsByMonth = <String, Earnings>{};
  final earningsErrors = <String, Object>{};
  final requestedMonths = <String>[];
  Completer<Earnings>? gate;
  final historyPages = <PayoutHistoryPage>[];
  Object? historyError;
  final requestedPages = <int>[];

  @override
  Future<Earnings> getEarnings(String month) {
    requestedMonths.add(month);
    if (gate != null) return gate!.future;
    final error = earningsErrors[month];
    if (error != null) return Future.error(error);
    return Future.value(earningsByMonth[month] ?? earnings(month));
  }

  @override
  Future<PayoutHistoryPage> getPayouts({int page = 1, int pageSize = 20}) {
    requestedPages.add(page);
    if (historyError != null) return Future.error(historyError!);
    return Future.value(historyPages[page - 1]);
  }
}

Future<void> pumpEarnings(WidgetTester tester, FakePayoutsService service, {DateTime? now}) async {
  tester.view.physicalSize = const Size(390, 2400);
  tester.view.devicePixelRatio = 1.0;
  addTearDown(() {
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
  await tester.pumpWidget(MaterialApp(
    theme: NordicTheme.lightTheme,
    home: PayoutScreen(service: service, now: () => now ?? DateTime.utc(2026, 10, 15, 3)),
  ));
  await tester.pumpAndSettle();
}

void main() {
  group('PayoutScreen (MOB-M6-04)', () {
    testWidgets('opens on the current Ho Chi Minh month and shows the take-home figure as "Thu nhập tháng này"', (tester) async {
      final service = FakePayoutsService();
      await pumpEarnings(tester, service);

      expect(service.requestedMonths, ['2026-10']);
      expect(find.text('Tháng 10/2026'), findsOneWidget);
      expect(find.text('Thu nhập tháng này'), findsOneWidget);
      expect(find.text('430.000 đ'), findsOneWidget);
      expect(find.textContaining('Số dư khả dụng'), findsNothing);
    });

    testWidgets('at 17:00 UTC on the last day the local month is already the next one', (tester) async {
      final service = FakePayoutsService();
      await pumpEarnings(tester, service, now: DateTime.utc(2026, 9, 30, 17));

      expect(service.requestedMonths, ['2026-10']);
    });

    testWidgets('shows the breakdown, the payout status with its hint and every job', (tester) async {
      final service = FakePayoutsService()..earningsByMonth['2026-10'] = earnings('2026-10', status: 'PENDING');
      await pumpEarnings(tester, service);

      expect(tester.widget<Text>(find.byKey(const Key('earnings-gross'))).data, '600.000 đ');
      expect(tester.widget<Text>(find.byKey(const Key('earnings-commission'))).data, '-120.000 đ');
      expect(tester.widget<Text>(find.byKey(const Key('earnings-penalty'))).data, '-50.000 đ');
      expect(find.text('Chờ chuyển khoản'), findsOneWidget);
      expect(find.textContaining('đang chờ Admin chuyển khoản'), findsOneWidget);
      expect(find.text('Đơn #10'), findsOneWidget);
      expect(find.text('Đơn #11'), findsOneWidget);
      expect(find.text('01/10/2026 00:30'), findsOneWidget); // 17:30 UTC is after midnight in Ho Chi Minh
      expect(find.text('Phí vắng mặt'), findsOneWidget);
      expect(find.byKey(const Key('job-net-1')), findsOneWidget);
    });

    testWidgets('a month with no jobs shows an empty state', (tester) async {
      final service = FakePayoutsService()..earningsByMonth['2026-10'] = earnings('2026-10', net: 0, jobs: const []);
      await pumpEarnings(tester, service);

      expect(find.byKey(const Key('earnings-empty')), findsOneWidget);
      expect(find.text('0 đ'), findsWidgets);
    });

    testWidgets('the previous button loads the earlier month and the next button is disabled on the current one', (tester) async {
      final service = FakePayoutsService();
      await pumpEarnings(tester, service);
      expect(tester.widget<IconButton>(find.byKey(const Key('earnings-next'))).onPressed, isNull);

      await tester.tap(find.byKey(const Key('earnings-prev')));
      await tester.pumpAndSettle();

      expect(service.requestedMonths, ['2026-10', '2026-09']);
      expect(find.text('Tháng 9/2026'), findsOneWidget);
      expect(tester.widget<IconButton>(find.byKey(const Key('earnings-next'))).onPressed, isNotNull);

      await tester.tap(find.byKey(const Key('earnings-next')));
      await tester.pumpAndSettle();
      expect(service.requestedMonths.last, '2026-10');
    });

    testWidgets('going back over New Year loads December of the previous year', (tester) async {
      final service = FakePayoutsService();
      await pumpEarnings(tester, service, now: DateTime.utc(2026, 1, 15));

      await tester.tap(find.byKey(const Key('earnings-prev')));
      await tester.pumpAndSettle();

      expect(service.requestedMonths.last, '2025-12');
      expect(find.text('Tháng 12/2025'), findsOneWidget);
    });

    testWidgets('a slow answer of an older month does not overwrite the month chosen after it', (tester) async {
      final service = FakePayoutsService();
      await pumpEarnings(tester, service);
      final slow = Completer<Earnings>();
      service.gate = slow;
      await tester.tap(find.byKey(const Key('earnings-prev'))); // 2026-09, slow
      await tester.pump();
      service.gate = null;
      await tester.tap(find.byKey(const Key('earnings-prev'))); // 2026-08, fast
      await tester.pumpAndSettle();
      slow.complete(earnings('2026-09', net: 111));
      await tester.pumpAndSettle();

      expect(find.text('Tháng 8/2026'), findsOneWidget);
      expect(find.text('111 đ'), findsNothing);
      expect(find.text('430.000 đ'), findsOneWidget);
    });

    testWidgets('an agency staff member (403) is told the agency is paid and gets no retry', (tester) async {
      final service = FakePayoutsService()..earningsErrors['2026-10'] = const ApiException(403, 'Forbidden');
      await pumpEarnings(tester, service);

      expect(find.textContaining('đại lý'), findsOneWidget);
      expect(find.text('Thử lại'), findsNothing);
    });

    testWidgets('a network failure shows the message and a retry that loads again', (tester) async {
      final service = FakePayoutsService()..earningsErrors['2026-10'] = const ApiException(0, 'Không kết nối được máy chủ.');
      await pumpEarnings(tester, service);
      expect(find.text('Không kết nối được máy chủ.'), findsOneWidget);

      service.earningsErrors.clear();
      await tester.tap(find.text('Thử lại'));
      await tester.pumpAndSettle();

      expect(service.requestedMonths, ['2026-10', '2026-10']);
      expect(find.text('430.000 đ'), findsOneWidget);
    });

    testWidgets('shows a loader while the month loads', (tester) async {
      final service = FakePayoutsService()..gate = Completer<Earnings>();
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });
      await tester.pumpWidget(MaterialApp(theme: NordicTheme.lightTheme, home: PayoutScreen(service: service, now: () => DateTime.utc(2026, 10, 15))));
      await tester.pump();

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
    });

    testWidgets('the history button opens the payout history', (tester) async {
      final service = FakePayoutsService()
        ..historyPages.add(const PayoutHistoryPage(items: [], page: 1, pageSize: 20, total: 0));
      await pumpEarnings(tester, service);

      await tester.ensureVisible(find.byKey(const Key('earnings-history')));
      await tester.tap(find.byKey(const Key('earnings-history')));
      await tester.pumpAndSettle();

      expect(find.text('Lịch sử giải ngân'), findsWidgets);
      expect(find.byKey(const Key('history-empty')), findsOneWidget);
    });
  });

  group('PayoutHistoryScreen', () {
    PayoutHistoryItem item(int id, String month, {String status = 'TRANSFERRED', DateTime? at}) =>
        PayoutHistoryItem(batchId: id, periodMonth: month, netAmount: id * 100000, itemStatus: status, transferredAt: at);

    Future<void> pumpHistory(WidgetTester tester, FakePayoutsService service, {int pageSize = 2}) async {
      tester.view.physicalSize = const Size(390, 1600);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });
      await tester.pumpWidget(MaterialApp(theme: NordicTheme.lightTheme, home: PayoutHistoryScreen(service: service, pageSize: pageSize)));
      await tester.pumpAndSettle();
    }

    testWidgets('lists the month, the net amount and when it was transferred, then loads more', (tester) async {
      final service = FakePayoutsService()
        ..historyPages.addAll([
          PayoutHistoryPage(items: [item(3, '2026-09', at: DateTime.utc(2026, 10, 3, 2)), item(2, '2026-08', status: 'PENDING')], page: 1, pageSize: 2, total: 3),
          PayoutHistoryPage(items: [item(1, '2026-07', at: DateTime.utc(2026, 8, 3, 2))], page: 2, pageSize: 2, total: 3),
        ]);
      await pumpHistory(tester, service);

      expect(find.text('Tháng 9/2026'), findsOneWidget);
      expect(find.text('Đã chuyển khoản 03/10/2026'), findsOneWidget);
      expect(find.text('Chờ chuyển khoản'), findsOneWidget);
      expect(find.text('300.000 đ'), findsOneWidget);
      expect(find.byKey(const Key('history-more')), findsOneWidget);

      await tester.tap(find.byKey(const Key('history-more')));
      await tester.pumpAndSettle();

      expect(service.requestedPages, [1, 2]);
      expect(find.text('Tháng 7/2026'), findsOneWidget);
      expect(find.byKey(const Key('history-more')), findsNothing);
    });

    testWidgets('has an empty state when nothing was ever paid', (tester) async {
      final service = FakePayoutsService()..historyPages.add(const PayoutHistoryPage(items: [], page: 1, pageSize: 2, total: 0));
      await pumpHistory(tester, service);

      expect(find.byKey(const Key('history-empty')), findsOneWidget);
    });

    testWidgets('a failure on the first page offers a retry that loads again', (tester) async {
      final service = FakePayoutsService()
        ..historyError = const ApiException(0, 'Không kết nối được máy chủ.')
        ..historyPages.add(PayoutHistoryPage(items: [item(1, '2026-07', at: DateTime.utc(2026, 8, 3))], page: 1, pageSize: 2, total: 1));
      await pumpHistory(tester, service);
      expect(find.text('Không kết nối được máy chủ.'), findsOneWidget);

      service.historyError = null;
      await tester.tap(find.text('Thử lại'));
      await tester.pumpAndSettle();

      expect(find.text('Tháng 7/2026'), findsOneWidget);
    });

    testWidgets('a failure on a later page keeps the loaded items and shows the message', (tester) async {
      final service = FakePayoutsService()
        ..historyPages.add(PayoutHistoryPage(items: [item(3, '2026-09', at: DateTime.utc(2026, 10, 3)), item(2, '2026-08', at: DateTime.utc(2026, 9, 3))], page: 1, pageSize: 2, total: 5));
      await pumpHistory(tester, service);
      service.historyError = const ApiException(500, 'Máy chủ lỗi.');

      await tester.tap(find.byKey(const Key('history-more')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('history-error')), findsOneWidget);
      expect(find.text('Tháng 9/2026'), findsOneWidget);
      expect(find.byKey(const Key('history-more')), findsOneWidget);
    });
  });
}
