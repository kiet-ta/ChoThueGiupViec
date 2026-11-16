import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_response.dart';
import 'package:mobile/core/realtime/realtime_service.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/dispatch/routes.dart';
import 'package:mobile/features/home/worker_shell.dart';
import 'package:mobile/features/home/worker_today_tab.dart';
import 'package:mobile/features/payouts/models/payout_models.dart';
import 'package:mobile/features/payouts/services/payouts_service.dart';
import 'package:mobile/features/workers/models/booking_slot_models.dart';
import 'package:mobile/features/workers/models/worker_assignment_models.dart';
import 'package:mobile/features/workers/screens/worker_slots_screen.dart';
import 'package:mobile/features/workers/services/workers_assignments_service.dart';
import 'package:mobile/features/workers/services/workers_profile_service.dart';
import 'package:mobile/features/workers/services/workers_slots_service.dart';

import '../workers/design_frame.dart';

/// AGENCY-WORKER-VIEW-01 (decisions D44): an agency worker is never offered a job, so the doors into the offers are gone (the centre
/// "Việc" button, the bell and the "Nhận việc" row of Hôm nay); the jobs of their shifts are still shown like a freelancer's.

class _Profile extends WorkersProfileService {
  _Profile({this.agency = false, this.failing = false});

  final bool agency;
  final bool failing;

  @override
  Future<WorkerSummary> getSummary() async {
    if (failing) throw Exception('offline');
    return (fullName: 'Thợ Demo', agencyWorker: agency);
  }

  @override
  Future<String?> getFullName() async => 'Thợ Demo';
}

class _Payouts implements IPayoutsService {
  @override
  Future<Earnings> getEarnings(String month) async => Earnings(
    periodMonth: month,
    jobCount: 3,
    grossAmount: 0,
    commissionAmount: 0,
    penaltyAmount: 0,
    netAmount: 0,
    payoutStatus: 'NOT_BUILT',
    jobs: const [],
  );

  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

class _Slots extends WorkersSlotsService {
  @override
  Future<ApiResponse<List<BookingSlotDto>>> getSlots({
    required String startDate,
    required String endDate,
  }) async => const ApiResponse<List<BookingSlotDto>>(
    success: true,
    message: 'OK',
    data: [],
  );
}

class _Assignments extends WorkersAssignmentsService {
  @override
  Future<ApiResponse<List<WorkerAssignmentDto>>> getAssignments({
    required String startDate,
    required String endDate,
  }) async => ApiResponse<List<WorkerAssignmentDto>>(
    success: true,
    message: 'OK',
    data: [
      WorkerAssignmentDto(
        assignmentId: 10002,
        orderId: 10002,
        orderCode: 'GV-10002',
        slotDate: '2026-10-12',
        shiftCode: 'SHIFT_MORNING',
        startTime: '08:00',
        endTime: '12:00',
        status: 'ASSIGNED',
        serviceTier: 'PREMIUM',
        district: 'Quận 1',
        payoutAmount: 128000,
      ),
    ],
  );
}

class _FakeRealtime implements IRealtimeService {
  final _controller = StreamController<RealtimeNotification>.broadcast();

  @override
  Stream<RealtimeNotification> get notifications => _controller.stream;

  @override
  Future<void> connect() async {}

  @override
  Future<void> disconnect() async {}

  @override
  Future<void> dispose() async => _controller.close();

  void push(RealtimeNotification n) => _controller.add(n);
}

const _tabs = [
  Text('tab-today'),
  Text('tab-slots'),
  Text('tab-income'),
  Text('tab-account'),
];

Future<void> _pumpShell(
  WidgetTester tester,
  WorkersProfileService profile, {
  IRealtimeService? realtime,
  Map<String, WidgetBuilder> routes = const {},
}) async {
  useDesignFrame(tester);
  await tester.pumpWidget(
    MaterialApp(
      theme: NordicTheme.lightTheme,
      routes: routes,
      home: WorkerShell(
        onSignOut: () {},
        workerId: 12,
        profile: profile,
        realtime: realtime,
        tabs: _tabs,
      ),
    ),
  );
  await tester.pumpAndSettle();
}

Future<void> _pumpToday(
  WidgetTester tester,
  WorkersProfileService profile,
  List<String> calls,
) async {
  useDesignFrame(tester);
  await tester.pumpWidget(
    MaterialApp(
      home: WorkerTodayTab(
        workerId: 12,
        payouts: _Payouts(),
        slots: _Slots(),
        profile: profile,
        now: () => DateTime(2026, 10, 12),
        onOpenOffers: () => calls.add('offers'),
        onShowSlots: () => calls.add('slots'),
      ),
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  group('the bar', () {
    testWidgets(
      'an agency worker has no "Việc" button and four equal tabs that still switch',
      (tester) async {
        await _pumpShell(tester, _Profile(agency: true));

        expect(find.byKey(const Key('worker-shell-job')), findsNothing);
        expect(find.text('Việc'), findsNothing);
        for (var i = 0; i < 4; i++) {
          expect(find.byKey(Key('worker-tab-$i')), findsOneWidget);
        }
        // Four items share the bar: equal widths, no hole in the middle.
        final widths = [
          for (var i = 0; i < 4; i++)
            tester.getSize(find.byKey(Key('worker-tab-$i'))).width,
        ];
        expect(widths.toSet().length, 1);
        expect(widths.first, closeTo(390 / 4, 1));

        for (var i = 1; i < 4; i++) {
          await tester.tap(find.byKey(Key('worker-tab-$i')));
          await tester.pump();
          expect(
            tester.widget<IndexedStack>(find.byType(IndexedStack)).index,
            i,
          );
        }
      },
    );

    for (final (name, profile) in <(String, WorkersProfileService Function())>[
      ('a freelancer', () => _Profile()),
      ('a profile that cannot be read', () => _Profile(failing: true)),
    ]) {
      testWidgets('$name keeps the centre "Việc" button', (tester) async {
        await _pumpShell(tester, profile());

        expect(find.byKey(const Key('worker-shell-job')), findsOneWidget);
        expect(find.text('Việc'), findsWidgets);
      });
    }

    testWidgets('a freelancer\'s "Việc" button opens the offers', (
      tester,
    ) async {
      await _pumpShell(
        tester,
        _Profile(),
        routes: {
          DispatchRoutes.offers: (c) => const Scaffold(body: Text('offers')),
        },
      );

      await tester.tap(find.byKey(const Key('worker-shell-job')));
      await tester.pumpAndSettle();

      expect(find.text('offers'), findsOneWidget);
    });

    testWidgets(
      '"Xem việc" of a realtime notice still opens the job of an agency worker',
      (tester) async {
        final realtime = _FakeRealtime();
        final opened = <Object?>[];
        await _pumpShell(
          tester,
          _Profile(agency: true),
          realtime: realtime,
          routes: {
            DispatchRoutes.checkIn: (c) {
              opened.add(ModalRoute.of(c)!.settings.arguments);
              return const Scaffold(body: Text('Chi tiết việc'));
            },
            DispatchRoutes.offers: (c) => const Scaffold(body: Text('offers')),
          },
        );

        realtime.push(
          const RealtimeNotification(
            topic: RealtimeTopics.autoAccepted,
            title: 'Việc mới',
            body: 'Bạn có việc mới lúc 08:00.',
            data: {'assignmentId': '42', 'orderId': '7'},
          ),
        );
        await tester.pump();
        await tester.pump(const Duration(milliseconds: 750));
        tester
            .widget<SnackBarAction>(
              find.byKey(const Key('worker-realtime-open-job')),
            )
            .onPressed();
        await tester.pumpAndSettle();

        expect(opened, [42]);
        expect(find.text('Chi tiết việc'), findsOneWidget);
        expect(find.text('offers'), findsNothing);
      },
    );
  });

  group('Hôm nay', () {
    testWidgets(
      'an agency worker has no bell and no "Nhận việc" row, the rest stays',
      (tester) async {
        final calls = <String>[];
        await _pumpToday(tester, _Profile(agency: true), calls);

        expect(find.byKey(const Key('worker-today-offers')), findsNothing);
        expect(find.byKey(const Key('worker-today-offer-row')), findsNothing);
        expect(find.text('Nhận việc'), findsNothing);
        expect(find.byKey(const Key('worker-today-name')), findsOneWidget);
        expect(find.byKey(const Key('worker-today-net')), findsOneWidget);
        expect(find.byKey(const Key('worker-today-jobs')), findsOneWidget);
        expect(find.byKey(const Key('worker-today-slots')), findsOneWidget);
      },
    );

    for (final (name, profile) in <(String, WorkersProfileService Function())>[
      ('a freelancer', () => _Profile()),
      ('a profile that cannot be read', () => _Profile(failing: true)),
    ]) {
      testWidgets(
        '$name keeps the bell and the row, and both open the offers',
        (tester) async {
          final calls = <String>[];
          await _pumpToday(tester, profile(), calls);

          expect(find.byKey(const Key('worker-today-offers')), findsOneWidget);
          expect(
            find.byKey(const Key('worker-today-offer-row')),
            findsOneWidget,
          );
          await tester.tap(find.byKey(const Key('worker-today-offers')));
          await tapVisible(
            tester,
            find.byKey(const Key('worker-today-offer-row')),
          );
          expect(calls, ['offers', 'offers']);
        },
      );
    }
  });

  testWidgets(
    'an agency worker still sees the job under its shift and its details, like a freelancer',
    (tester) async {
      useDesignFrame(tester);
      await tester.pumpWidget(
        MaterialApp(
          home: WorkerSlotsScreen(
            slotsService: _Slots(),
            assignmentsService: _Assignments(),
            profileService: _Profile(agency: true),
            initialDate: DateTime(2026, 10, 12),
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('order-10002')), findsOneWidget);
      expect(find.text('GV-10002'), findsOneWidget);
      await tapVisible(tester, find.byKey(const Key('order-10002')));
      expect(find.byKey(const Key('order-sheet-10002')), findsOneWidget);
      expect(find.text('Quận 1'), findsWidgets);
      expect(find.byKey(const Key('order-sheet-route')), findsOneWidget);
      expect(find.byKey(const Key('order-sheet-action')), findsOneWidget);
      expect(find.byKey(const Key('open-worker-cancel')), findsOneWidget);
      expect(find.byType(Switch), findsNothing); // D43: the roster is read-only
    },
  );
}
