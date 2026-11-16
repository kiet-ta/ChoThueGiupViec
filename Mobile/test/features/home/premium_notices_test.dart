import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/realtime/realtime_service.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/booking/models/booking_models.dart';
import 'package:mobile/features/booking/routes.dart';
import 'package:mobile/features/dispatch/routes.dart';
import 'package:mobile/features/home/customer_shell.dart';
import 'package:mobile/features/home/worker_shell.dart';

import '../workers/design_frame.dart';

/// PREM-MOB-01 (#954, decision D38): what the two apps show for the Premium notices that arrive over the realtime channel:
/// the customer's apology (`premium.order_cancelled`), the worker's check-in reminder (`dispatch.checkin_reminder`) and the urgent
/// rescue notice (`dispatch.rescue_assigned`). Each stays on screen until it is closed or its button is pressed.
class _FakeRealtime implements IRealtimeService {
  final _controller = StreamController<RealtimeNotification>.broadcast();
  int connects = 0;
  int disconnects = 0;

  @override
  Stream<RealtimeNotification> get notifications => _controller.stream;

  @override
  Future<void> connect() async => connects++;

  @override
  Future<void> disconnect() async => disconnects++;

  @override
  Future<void> dispose() async => _controller.close();

  void push(RealtimeNotification n) => _controller.add(n);
}

class _CountingTab extends StatefulWidget {
  final String label;
  final List<String> inits;

  const _CountingTab(this.label, this.inits);

  @override
  State<_CountingTab> createState() => _CountingTabState();
}

class _CountingTabState extends State<_CountingTab> {
  @override
  void initState() {
    super.initState();
    widget.inits.add(widget.label);
  }

  @override
  Widget build(BuildContext context) => Center(child: Text(widget.label));
}

const _apology = RealtimeNotification(
  topic: RealtimeTopics.premiumOrderCancelled,
  title: 'TỔ ẤM xin lỗi bạn',
  body: 'Xin lỗi bạn, ca dọn nhà của bạn không thể bắt đầu vì đối tác chưa đủ người làm đúng giờ. Đơn đã được huỷ và hoàn 100% (780.000 đ). TỔ ẤM chân thành xin lỗi vì sự bất tiện này.',
  data: {'orderId': '19'},
);

void main() {
  late _FakeRealtime realtime;
  late List<String> inits;
  late List<Object?> opened;

  setUp(() {
    realtime = _FakeRealtime();
    inits = [];
    opened = [];
  });

  Future<void> pumpCustomer(WidgetTester tester) async {
    useDesignFrame(tester);
    await tester.pumpWidget(
      MaterialApp(
        theme: NordicTheme.lightTheme,
        routes: {
          BookingRoutes.bookingDetail: (context) {
            opened.add(ModalRoute.of(context)!.settings.arguments);
            return const Scaffold(body: Text('Chi tiết đơn'));
          },
        },
        home: CustomerShell(
          onSignOut: () {},
          realtime: realtime,
          tabs: [
            _CountingTab('Trang chủ', inits),
            _CountingTab('Đơn', inits),
            const SizedBox(),
            const SizedBox(),
          ],
        ),
      ),
    );
    await tester.pump();
  }

  Future<void> pumpWorker(WidgetTester tester) async {
    useDesignFrame(tester);
    await tester.pumpWidget(
      MaterialApp(
        theme: NordicTheme.lightTheme,
        routes: {
          DispatchRoutes.checkIn: (context) {
            opened.add(ModalRoute.of(context)!.settings.arguments);
            return const Scaffold(body: Text('Chi tiết việc'));
          },
        },
        home: WorkerShell(
          onSignOut: () {},
          workerId: 55,
          realtime: realtime,
          tabs: [
            _CountingTab('Hôm nay', inits),
            _CountingTab('Lịch', inits),
            const SizedBox(),
            const SizedBox(),
          ],
        ),
      ),
    );
    await tester.pump();
  }

  Future<void> settle(WidgetTester tester) async {
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 750));
  }

  group('customer: the apology of a cancelled Premium order', () {
    testWidgets(
      'shows the server title and text, stays until closed, and the lists reload',
      (tester) async {
        await pumpCustomer(tester);
        expect(realtime.connects, 1);
        // The customer shell builds every tab at once.
        expect(inits, ['Trang chủ', 'Đơn']);

        realtime.push(_apology);
        await settle(tester);

        expect(find.byKey(const Key('realtime-notice')), findsOneWidget);
        expect(find.text('TỔ ẤM xin lỗi bạn'), findsOneWidget);
        expect(find.textContaining('hoàn 100% (780.000 đ)'), findsOneWidget);
        expect(
          inits,
          hasLength(4),
        ); // every tab was rebuilt: the lists say "Đã huỷ"

        // It does not vanish on its own, unlike a snack bar.
        await tester.pump(const Duration(seconds: 30));
        expect(find.byKey(const Key('realtime-notice')), findsOneWidget);

        await tester.tap(find.byKey(const Key('realtime-notice-close')));
        await tester.pumpAndSettle();
        expect(find.byKey(const Key('realtime-notice')), findsNothing);
      },
    );

    testWidgets('"Xem đơn" closes the notice and opens the cancelled order', (
      tester,
    ) async {
      await pumpCustomer(tester);
      realtime.push(_apology);
      await settle(tester);

      await tester.tap(find.byKey(const Key('realtime-notice-action')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('realtime-notice')), findsNothing);
      expect(opened, hasLength(1));
      expect((opened.single as OrderDetailArgs).orderId, 19);
      expect(find.text('Chi tiết đơn'), findsOneWidget);
    });

    testWidgets(
      'without an order id there is no button, and without a title the apology still has one',
      (tester) async {
        await pumpCustomer(tester);
        realtime.push(
          const RealtimeNotification(
            topic: RealtimeTopics.premiumOrderCancelled,
            title: '',
            body: 'Đơn đã được huỷ.',
          ),
        );
        await settle(tester);

        expect(find.text('TỔ ẤM xin lỗi bạn'), findsOneWidget);
        expect(find.text('Đơn đã được huỷ.'), findsOneWidget);
        expect(find.byKey(const Key('realtime-notice-action')), findsNothing);
      },
    );

    testWidgets('a newer notice replaces the one on screen', (tester) async {
      await pumpCustomer(tester);
      realtime.push(_apology);
      await settle(tester);
      realtime.push(
        const RealtimeNotification(
          topic: RealtimeTopics.premiumOrderCancelled,
          title: 'Đơn khác',
          body: 'Nội dung khác.',
          data: {'orderId': '20'},
        ),
      );
      await settle(tester);

      expect(find.byKey(const Key('realtime-notice')), findsOneWidget);
      expect(find.text('Đơn khác'), findsOneWidget);
      expect(find.text('TỔ ẤM xin lỗi bạn'), findsNothing);
    });

    testWidgets('other topics are ignored', (tester) async {
      await pumpCustomer(tester);
      realtime.push(
        const RealtimeNotification(
          topic: 'payment.status',
          title: 'Paid',
          body: 'x',
        ),
      );
      await settle(tester);

      expect(find.byKey(const Key('realtime-notice')), findsNothing);
      expect(inits, ['Trang chủ', 'Đơn']);
    });

    testWidgets(
      'leaving the shell (sign-out) disconnects and removes the notice',
      (tester) async {
        await pumpCustomer(tester);
        realtime.push(_apology);
        await settle(tester);

        await tester.pumpWidget(const MaterialApp(home: Text('Chào mừng')));
        await tester.pump();

        expect(realtime.disconnects, 1);
        expect(find.byKey(const Key('realtime-notice')), findsNothing);
      },
    );

    testWidgets('a shell with replaced tabs and no channel stays offline', (
      tester,
    ) async {
      useDesignFrame(tester);
      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: CustomerShell(
            onSignOut: () {},
            tabs: const [SizedBox(), SizedBox(), SizedBox(), SizedBox()],
          ),
        ),
      );
      await tester.pump();

      expect(find.byKey(const Key('customer-shell-book')), findsOneWidget);
    });
  });

  group('worker: the check-in reminder', () {
    testWidgets(
      'says "Còn 30 phút, hãy di chuyển", stays until closed, and "Xem việc" opens the job',
      (tester) async {
        await pumpWorker(tester);
        realtime.push(
          const RealtimeNotification(
            topic: RealtimeTopics.checkInReminder,
            title: 'Sắp đến giờ làm',
            body: 'Còn khoảng 30 phút nữa đến ca của bạn. Hãy di chuyển đến địa chỉ của khách ngay để check-in đúng giờ.',
            data: {
              'assignmentId': '42',
              'orderId': '7',
              'shiftStartUtc': '2026-11-20T01:00:00.0000000Z',
            },
          ),
        );
        await settle(tester);

        expect(find.byKey(const Key('realtime-notice')), findsOneWidget);
        expect(find.text('Còn 30 phút, hãy di chuyển'), findsOneWidget);
        expect(find.textContaining('check-in đúng giờ'), findsOneWidget);
        // A reminder is not urgent: no check-in deadline line and no rescue look.
        expect(find.byKey(const Key('realtime-notice-detail')), findsNothing);

        await tester.pump(const Duration(seconds: 30));
        expect(find.byKey(const Key('realtime-notice')), findsOneWidget);

        await tester.tap(find.byKey(const Key('realtime-notice-action')));
        await tester.pumpAndSettle();
        expect(opened, [42]);
        expect(find.text('Chi tiết việc'), findsOneWidget);
        expect(find.byKey(const Key('realtime-notice')), findsNothing);
      },
    );

    testWidgets('a reminder without a job id has no button', (tester) async {
      await pumpWorker(tester);
      realtime.push(
        const RealtimeNotification(
          topic: RealtimeTopics.checkInReminder,
          title: 'Sắp đến giờ làm',
          body: 'Hãy di chuyển.',
        ),
      );
      await settle(tester);

      expect(find.text('Còn 30 phút, hãy di chuyển'), findsOneWidget);
      expect(find.byKey(const Key('realtime-notice-action')), findsNothing);
    });
  });

  group('worker: the urgent rescue notice', () {
    testWidgets(
      'is urgent, shows the check-in deadline, reloads the jobs and opens the job',
      (tester) async {
        await pumpWorker(tester);
        expect(inits, ['Hôm nay']);
        final by = DateTime.utc(2026, 11, 20, 1, 5);
        realtime.push(
          RealtimeNotification(
            topic: RealtimeTopics.rescueAssigned,
            title: 'Ca cứu hộ GẤP',
            body: 'Bạn vừa nhận một ca cứu hộ gấp. Hãy di chuyển ngay. Nếu không kịp, bạn có thể hủy ca mà không bị phạt.',
            data: {
              'assignmentId': '77',
              'orderId': '9',
              'urgent': 'true',
              'checkInBy': by.toIso8601String(),
            },
          ),
        );
        await settle(tester);

        expect(find.byKey(const Key('realtime-notice')), findsOneWidget);
        expect(find.text('Ca cứu hộ GẤP'), findsOneWidget);
        final local = by.toLocal();
        final hhmm =
            '${local.hour.toString().padLeft(2, '0')}:${local.minute.toString().padLeft(2, '0')}';
        expect(
          tester
              .widget<Text>(find.byKey(const Key('realtime-notice-detail')))
              .data,
          'Check-in trước $hhmm',
        );
        expect(inits, ['Hôm nay', 'Hôm nay']); // the jobs reloaded

        // The urgent look: the card is tinted with the error container, not the white card.
        final card = tester.widget<Container>(
          find.byKey(const Key('realtime-notice')),
        );
        expect((card.decoration as BoxDecoration).color, isNot(Colors.white));

        await tester.tap(find.byKey(const Key('realtime-notice-action')));
        await tester.pumpAndSettle();
        expect(opened, [77]);
      },
    );

    testWidgets(
      'without a deadline the notice still shows, just without the deadline line',
      (tester) async {
        await pumpWorker(tester);
        realtime.push(
          const RealtimeNotification(
            topic: RealtimeTopics.rescueAssigned,
            title: '',
            body: 'Bạn vừa nhận một ca cứu hộ gấp.',
            data: {'assignmentId': '77'},
          ),
        );
        await settle(tester);

        expect(find.text('Ca cứu hộ gấp'), findsOneWidget);
        expect(find.byKey(const Key('realtime-notice-detail')), findsNothing);
        expect(find.byKey(const Key('realtime-notice-action')), findsOneWidget);
      },
    );

    testWidgets('closing it leaves the app as it was', (tester) async {
      await pumpWorker(tester);
      realtime.push(
        const RealtimeNotification(
          topic: RealtimeTopics.rescueAssigned,
          title: 'Ca cứu hộ GẤP',
          body: 'x',
          data: {'assignmentId': '77'},
        ),
      );
      await settle(tester);

      await tester.tap(find.byKey(const Key('realtime-notice-close')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('realtime-notice')), findsNothing);
      expect(opened, isEmpty);
    });
  });
}
