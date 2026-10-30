import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_response.dart';
import 'package:mobile/features/home/worker_shell.dart';
import 'package:mobile/features/home/worker_today_tab.dart';
import 'package:mobile/features/payouts/models/payout_models.dart';
import 'package:mobile/features/payouts/services/payouts_service.dart';
import 'package:mobile/features/workers/models/booking_slot_models.dart';
import 'package:mobile/features/workers/screens/worker_screen.dart';
import 'package:mobile/features/workers/services/workers_profile_service.dart';
import 'package:mobile/features/workers/services/workers_slots_service.dart';

import '../workers/design_frame.dart';

class _Payouts implements IPayoutsService {
  final requested = <String>[];

  @override
  Future<Earnings> getEarnings(String month) {
    requested.add(month);
    return Future.value(
      Earnings(
        periodMonth: month,
        jobCount: 18,
        grossAmount: 5200000,
        commissionAmount: 1040000,
        penaltyAmount: 0,
        netAmount: 4160000,
        payoutStatus: 'NOT_BUILT',
        jobs: const [],
      ),
    );
  }

  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

class _Slots extends WorkersSlotsService {
  @override
  Future<ApiResponse<List<BookingSlotDto>>> getSlots({
    required String startDate,
    required String endDate,
  }) async => ApiResponse<List<BookingSlotDto>>(
    success: true,
    message: 'OK',
    data: [
      BookingSlotDto(
        slotId: 1,
        workerId: 12,
        slotDate: startDate,
        shiftCode: 'SHIFT_AFTERNOON',
        startTime: '13:00',
        endTime: '17:00',
        isActive: true,
      ),
    ],
  );
}

class _Profile extends WorkersProfileService {
  @override
  Future<String?> getFullName() async => 'Nguyễn Văn Thợi';
}

class _SummaryProfile extends WorkersProfileService {
  final String? name;
  final bool agency;

  _SummaryProfile({this.name, this.agency = false});

  @override
  Future<WorkerSummary> getSummary() async =>
      (fullName: name, agencyWorker: agency);
}

class _FailingProfile extends WorkersProfileService {
  @override
  Future<WorkerSummary> getSummary() async => throw Exception('offline');
}

void main() {
  testWidgets('Today tab shows the month earnings and the active slots', (
    tester,
  ) async {
    useDesignFrame(tester);
    final payouts = _Payouts();
    await tester.pumpWidget(
      MaterialApp(
        home: WorkerTodayTab(
          workerId: 12,
          payouts: payouts,
          slots: _Slots(),
          now: () => DateTime(2026, 10, 26),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(payouts.requested, ['2026-10']);
    expect(find.text('Thợ #12'), findsOneWidget);
    expect(find.text('Tháng 10'), findsOneWidget);
    expect(find.text('4.160.000 đ'), findsOneWidget);
    expect(find.text('18'), findsOneWidget);
    Container cell(String key) => tester.widget<Container>(
      find.descendant(
        of: find.byKey(Key(key)),
        matching: find.byType(Container),
      ),
    );
    final active = cell('week-cell-2026-10-26-SHIFT_AFTERNOON');
    final off = cell('week-cell-2026-10-26-SHIFT_MORNING');
    expect(
      (active.decoration as BoxDecoration).color,
      isNot((off.decoration as BoxDecoration).color),
    );
    expect(find.text('Sáng'), findsOneWidget);
    expect(find.text('Chiều'), findsOneWidget);
    expect(find.text('Tối'), findsOneWidget);
  });

  testWidgets('Today tab shows the worker name, and a cell opens that day', (
    tester,
  ) async {
    useDesignFrame(tester);
    final opened = <DateTime>[];
    await tester.pumpWidget(
      MaterialApp(
        home: WorkerTodayTab(
          workerId: 12,
          payouts: _Payouts(),
          slots: _Slots(),
          profile: _Profile(),
          onShowSlotsOn: opened.add,
          now: () => DateTime(2026, 10, 26),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Nguyễn Văn Thợi'), findsOneWidget);
    expect(find.text('Thợ #12'), findsNothing);

    await tapVisible(
      tester,
      find.byKey(const Key('week-cell-2026-10-28-SHIFT_MORNING')),
    );
    expect(opened, [DateTime(2026, 10, 28)]);
  });

  testWidgets('Today tab links open the offers, the slots and the income', (
    tester,
  ) async {
    useDesignFrame(tester);
    final calls = <String>[];
    await tester.pumpWidget(
      MaterialApp(
        home: WorkerTodayTab(
          workerId: 12,
          payouts: _Payouts(),
          slots: _Slots(),
          onOpenOffers: () => calls.add('offers'),
          onShowSlots: () => calls.add('slots'),
          onShowIncome: () => calls.add('income'),
        ),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('worker-today-offers')));
    await tapVisible(tester, find.byKey(const Key('worker-today-slots')));
    await tapVisible(tester, find.byKey(const Key('worker-today-income')));

    expect(calls, ['offers', 'slots', 'income']);
  });

  testWidgets('WorkerShell switches tabs from the bottom bar', (tester) async {
    useDesignFrame(tester);
    await tester.pumpWidget(
      MaterialApp(
        home: WorkerShell(
          workerId: 12,
          onSignOut: () {},
          tabs: const [
            Text('tab-today'),
            Text('tab-slots'),
            Text('tab-income'),
            Text('tab-account'),
          ],
        ),
      ),
    );

    for (final (key, text) in [
      ('worker-tab-1', 'tab-slots'),
      ('worker-tab-2', 'tab-income'),
      ('worker-tab-3', 'tab-account'),
      ('worker-tab-0', 'tab-today'),
    ]) {
      await tester.tap(find.byKey(Key(key)));
      await tester.pump();
      expect(
        tester.widget<IndexedStack>(find.byType(IndexedStack)).index,
        int.parse(key.split('-').last),
      );
      expect(find.text(text, skipOffstage: false), findsOneWidget);
    }
  });

  for (final (name, make, shown)
      in <(String, WorkersProfileService Function(), bool)>[
        (
          'an agency worker does not see the eKYC row',
          () => _SummaryProfile(agency: true),
          false,
        ),
        ('a freelancer sees the eKYC row', () => _SummaryProfile(), true),
        (
          'a profile that cannot be read keeps the eKYC row',
          () => _FailingProfile(),
          true,
        ),
      ]) {
    testWidgets('Account tab: $name', (tester) async {
      useDesignFrame(tester);
      await tester.pumpWidget(
        MaterialApp(
          home: WorkerScreen(workerId: 12, profile: make(), onSignOut: () {}),
        ),
      );
      await tester.pumpAndSettle();

      expect(
        find.byKey(const Key('worker-account-ekyc')),
        shown ? findsOneWidget : findsNothing,
      );
      // The other rows are never affected.
      for (final k in ['roster', 'disputes']) {
        expect(find.byKey(Key('worker-account-$k')), findsOneWidget);
      }
      expect(find.byKey(const Key('home-sign-out')), findsOneWidget);
    });
  }

  for (final agency in [false, true]) {
    testWidgets(
      'Account tab shows the worker name, not the id (${agency ? "agency worker" : "freelancer"})',
      (tester) async {
        useDesignFrame(tester);
        await tester.pumpWidget(
          MaterialApp(
            home: WorkerScreen(
              workerId: 12,
              profile: _SummaryProfile(name: 'Nguyễn Văn Thợi', agency: agency),
            ),
          ),
        );
        await tester.pumpAndSettle();

        expect(
          tester
              .widget<Text>(find.byKey(const Key('worker-account-name')))
              .data,
          'Nguyễn Văn Thợi',
        );
        expect(find.textContaining('Thợ #'), findsNothing);
      },
    );
  }

  testWidgets(
    'Account tab keeps the placeholder when the profile has no name',
    (tester) async {
      useDesignFrame(tester);
      await tester.pumpWidget(
        MaterialApp(
          home: WorkerScreen(workerId: 12, profile: _FailingProfile()),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Thợ #12'), findsOneWidget);
    },
  );

  testWidgets('Account tab shows the worker and the links, sign-out last', (
    tester,
  ) async {
    useDesignFrame(tester);
    var out = 0;
    await tester.pumpWidget(
      MaterialApp(home: WorkerScreen(workerId: 12, onSignOut: () => out++)),
    );

    expect(find.byKey(const Key('worker-account-name')), findsOneWidget);
    for (final k in ['ekyc', 'roster', 'disputes']) {
      expect(find.byKey(Key('worker-account-$k')), findsOneWidget);
    }
    await tapVisible(tester, find.byKey(const Key('home-sign-out')));
    expect(out, 1);
  });
}
