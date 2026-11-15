import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/realtime/realtime_service.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/dispatch/routes.dart';
import 'package:mobile/features/home/worker_shell.dart';

import '../workers/design_frame.dart';

/// MOB-RT-01 (#893): while the worker app is open, `dispatch.auto_accepted` shows the server's title and body, reloads Hôm nay and
/// Lịch, and opens the job. Other topics are ignored.
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

/// Counts how often a tab was built from scratch (a reload gives it a new key).
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

void main() {
  late _FakeRealtime realtime;
  late List<String> inits;
  late List<Object?> openedJobs;

  Future<void> pumpShell(WidgetTester tester) async {
    useDesignFrame(tester);
    await tester.pumpWidget(
      MaterialApp(
        theme: NordicTheme.lightTheme,
        routes: {
          DispatchRoutes.checkIn: (context) {
            openedJobs.add(ModalRoute.of(context)!.settings.arguments);
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

  setUp(() {
    realtime = _FakeRealtime();
    inits = [];
    openedJobs = [];
  });

  testWidgets(
    'auto-accepted job: notice with the server text, tabs reloaded, "Xem việc" opens the job',
    (tester) async {
      await pumpShell(tester);
      expect(realtime.connects, 1);
      expect(inits, ['Hôm nay']);

      realtime.push(
        const RealtimeNotification(
          topic: RealtimeTopics.autoAccepted,
          title: 'Đã tự nhận việc',
          body: 'Bạn có việc mới lúc 08:00, T6 20/11.',
          data: {'assignmentId': '42', 'orderId': '7'},
        ),
      );
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 750));

      expect(find.byKey(const Key('worker-realtime-notice')), findsOneWidget);
      expect(find.text('Đã tự nhận việc'), findsOneWidget);
      expect(find.text('Bạn có việc mới lúc 08:00, T6 20/11.'), findsOneWidget);
      expect(inits, ['Hôm nay', 'Hôm nay']); // Hôm nay reloaded

      // The action itself (the test surface's docked button overlay gets in the way of a synthetic tap).
      tester
          .widget<SnackBarAction>(
            find.byKey(const Key('worker-realtime-open-job')),
          )
          .onPressed();
      await tester.pumpAndSettle();
      expect(openedJobs, [42]);
      expect(find.text('Chi tiết việc'), findsOneWidget);
    },
  );

  testWidgets('other topics are ignored', (tester) async {
    await pumpShell(tester);

    realtime.push(
      const RealtimeNotification(
        topic: 'payment.status',
        title: 'Paid',
        body: 'x',
      ),
    );
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 750));

    expect(find.byKey(const Key('worker-realtime-notice')), findsNothing);
    expect(inits, ['Hôm nay']);
  });

  testWidgets('a notice without a job id has no "Xem việc"', (tester) async {
    await pumpShell(tester);

    realtime.push(
      const RealtimeNotification(
        topic: RealtimeTopics.autoAccepted,
        title: 'Đã tự nhận việc',
        body: 'Mở ứng dụng để xem.',
      ),
    );
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 750));

    expect(find.byKey(const Key('worker-realtime-notice')), findsOneWidget);
    expect(find.byKey(const Key('worker-realtime-open-job')), findsNothing);
  });

  testWidgets('leaving the shell (sign-out) disconnects', (tester) async {
    await pumpShell(tester);
    await tester.pumpWidget(const MaterialApp(home: Text('Chào mừng')));
    expect(realtime.disconnects, 1);
  });
}
