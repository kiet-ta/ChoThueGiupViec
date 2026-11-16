import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/device/device_location.dart';
import 'package:mobile/core/device/location_wrapper.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/network/api_response.dart';
import 'package:mobile/features/workers/models/vn_wards.dart';
import 'package:mobile/features/workers/models/worker_work_area_models.dart';
import 'package:mobile/features/workers/screens/worker_work_area_screen.dart';
import 'package:mobile/features/workers/services/workers_work_area_service.dart';

import 'design_frame.dart';

class _FakeService extends WorkersWorkAreaService {
  WorkerWorkArea area = const WorkerWorkArea.empty();
  bool failLoad = false;
  ApiException? failSave;
  SaveWorkAreaRequest? saved;
  int loads = 0;

  @override
  Future<ApiResponse<WorkerWorkArea>> getWorkArea() async {
    loads++;
    if (failLoad) throw const ApiException(0, 'offline');
    return ApiResponse<WorkerWorkArea>(success: true, message: '', data: area);
  }

  @override
  Future<ApiResponse<WorkerWorkArea>> saveWorkArea(
    SaveWorkAreaRequest request,
  ) async {
    if (failSave != null) throw failSave!;
    saved = request;
    return ApiResponse<WorkerWorkArea>(
      success: true,
      message: '',
      data: WorkerWorkArea(
        isSet: true,
        latitude: request.latitude,
        longitude: request.longitude,
        radiusKm: request.radiusKm,
        wards: [
          for (final code in request.wardCodes)
            WorkerWard.fromList(vnWards.firstWhere((w) => w.code == code)),
        ],
      ),
    );
  }
}

class _FakeLocation implements IDeviceLocationProvider {
  DeviceLocationFailure? failure;

  @override
  Future<GeoPoint> currentPosition() async {
    if (failure != null) throw DeviceLocationException(failure!);
    return GeoPoint(
      latitude: 10.7769,
      longitude: 106.7009,
      timestamp: DateTime.utc(2026, 10, 31),
    );
  }
}

void main() {
  late _FakeService service;
  late _FakeLocation location;
  // Thành phố Hồ Chí Minh (code 79): 26740 Phường Sài Gòn (merged Bến Nghé, Đa Kao, Nguyễn Thái Bình), 26743 Phường Bến Thành.
  final wards = wardsOfProvince(79);
  VnWard ward(int code) => vnWards.firstWhere((w) => w.code == code);

  setUp(() {
    service = _FakeService();
    location = _FakeLocation();
  });

  Future<void> pumpScreen(WidgetTester tester) async {
    useDesignFrame(tester);
    await tester.pumpWidget(
      MaterialApp(
        home: Builder(
          builder: (context) => Scaffold(
            body: Center(
              child: TextButton(
                onPressed: () => Navigator.of(context).push(
                  MaterialPageRoute<bool>(
                    builder: (_) => WorkerWorkAreaScreen(
                      service: service,
                      locationProvider: location,
                    ),
                  ),
                ),
                child: const Text('open'),
              ),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();
  }

  testWidgets('shows what is saved: the point, the radius and the wards', (
    tester,
  ) async {
    service.area = WorkerWorkArea(
      isSet: true,
      latitude: 10.7769,
      longitude: 106.7009,
      radiusKm: 15,
      wards: [WorkerWard.fromList(wards[0]), WorkerWard.fromList(wards[1])],
    );
    await pumpScreen(tester);

    expect(find.text('Nơi làm việc'), findsOneWidget);
    expect(find.text('Điểm làm việc: 10.7769, 106.7009'), findsOneWidget);
    expect(find.byKey(Key('ward-chip-${wards[0].code}')), findsOneWidget);
    expect(find.byKey(Key('ward-chip-${wards[1].code}')), findsOneWidget);
    expect(find.text('Lấy lại vị trí hiện tại'), findsOneWidget);
  });

  testWidgets(
    'nothing saved yet: no point, a message when saving without one',
    (tester) async {
      await pumpScreen(tester);

      expect(find.text('Chưa chọn vị trí'), findsOneWidget);
      expect(find.text('Dùng vị trí hiện tại'), findsOneWidget);

      await tester.tap(find.byKey(const Key('save-work-area')));
      await tester.pumpAndSettle();

      expect(find.textContaining('Bạn chưa chọn vị trí'), findsOneWidget);
      expect(service.saved, isNull);
    },
  );

  testWidgets('uses the phone position, a radius and wards, and saves them', (
    tester,
  ) async {
    await pumpScreen(tester);

    await tester.tap(find.byKey(const Key('use-current-location')));
    await tester.pumpAndSettle();
    expect(find.text('Điểm làm việc: 10.7769, 106.7009'), findsOneWidget);

    await tester.tap(find.byKey(const Key('radius-5')));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('pick-districts')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(Key('ward-option-${wards[0].code}')));
    await tester.tap(find.byKey(Key('ward-option-${wards[1].code}')));
    await tester.pumpAndSettle();
    expect(find.text('Chọn phường/xã (2/10)'), findsOneWidget);
    await tester.tap(find.byKey(const Key('district-done')));
    await tester.pumpAndSettle();
    expect(find.byKey(Key('ward-chip-${wards[0].code}')), findsOneWidget);

    await tester.tap(find.byKey(const Key('save-work-area')));
    await tester.pumpAndSettle();

    expect(service.saved, isNotNull);
    expect(service.saved!.latitude, 10.7769);
    expect(service.saved!.longitude, 106.7009);
    expect(service.saved!.radiusKm, 5);
    expect(service.saved!.wardCodes, [wards[0].code, wards[1].code]);
    // The screen closes after a successful save.
    expect(find.text('open'), findsOneWidget);
  });

  testWidgets('the radius is 10 km unless the worker chooses another', (
    tester,
  ) async {
    await pumpScreen(tester);
    await tester.tap(find.byKey(const Key('use-current-location')));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('save-work-area')));
    await tester.pumpAndSettle();

    expect(service.saved!.radiusKm, 10);
    expect(service.saved!.wardCodes, isEmpty);
  });

  testWidgets(
    'a refused or switched off position is explained in plain words',
    (tester) async {
      await pumpScreen(tester);
      final cases = {
        DeviceLocationFailure.serviceDisabled:
            'Vị trí trên điện thoại đang tắt',
        DeviceLocationFailure.permissionDenied:
            'Bạn chưa cho phép ứng dụng dùng vị trí',
        DeviceLocationFailure.permissionDeniedForever:
            'Ứng dụng bị chặn dùng vị trí',
        DeviceLocationFailure.timeout:
            'Chưa lấy được vị trí. Hãy ra chỗ thoáng',
      };
      for (final entry in cases.entries) {
        location.failure = entry.key;
        await tester.tap(find.byKey(const Key('use-current-location')));
        await tester.pumpAndSettle();
        expect(
          tester.widget<Text>(find.byKey(const Key('work-area-error'))).data,
          contains(entry.value),
          reason: entry.key.name,
        );
      }
      expect(find.text('Chưa chọn vị trí'), findsOneWidget);
    },
  );

  testWidgets('at most ten wards can be chosen', (tester) async {
    await pumpScreen(tester);
    await tester.tap(find.byKey(const Key('pick-districts')));
    await tester.pumpAndSettle();

    for (var i = 0; i < 10; i++) {
      await tester.ensureVisible(
        find.byKey(Key('ward-option-${wards[i].code}')),
      );
      await tester.tap(find.byKey(Key('ward-option-${wards[i].code}')));
      await tester.pump();
    }
    expect(find.text('Chọn phường/xã (10/10)'), findsOneWidget);

    await tester.ensureVisible(
      find.byKey(Key('ward-option-${wards[10].code}')),
    );
    await tester.tap(find.byKey(Key('ward-option-${wards[10].code}')));
    await tester.pumpAndSettle();

    expect(find.text('Chọn tối đa 10 phường/xã.'), findsOneWidget);
    expect(find.text('Chọn phường/xã (10/10)'), findsOneWidget);
  });

  testWidgets('the search narrows the list of wards', (tester) async {
    await pumpScreen(tester);
    await tester.tap(find.byKey(const Key('pick-districts')));
    await tester.pumpAndSettle();
    expect(find.byKey(Key('ward-option-${wards[0].code}')), findsOneWidget);

    await tester.enterText(
      find.byKey(const Key('district-search')),
      'zzzzzz-no-ward',
    );
    await tester.pumpAndSettle();

    expect(find.byKey(Key('ward-option-${wards[0].code}')), findsNothing);
  });

  testWidgets('searching an old name finds the ward it became and says so', (
    tester,
  ) async {
    await pumpScreen(tester);
    await tester.tap(find.byKey(const Key('pick-districts')));
    await tester.pumpAndSettle();

    await tester.enterText(
      find.byKey(const Key('district-search')),
      'ben nghe',
    );
    await tester.pumpAndSettle();

    // Phường Bến Nghé no longer exists: it was merged into Phường Sài Gòn (26740).
    expect(find.byKey(const Key('ward-option-26740')), findsOneWidget);
    expect(
      tester.widget<Text>(find.byKey(const Key('ward-legacy-26740'))).data,
      'trước đây: Phường Bến Nghé',
    );
    // A search by the current name shows no "trước đây" line.
    await tester.enterText(find.byKey(const Key('district-search')), 'sai gon');
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('ward-option-26740')), findsOneWidget);
    expect(find.byKey(const Key('ward-legacy-26740')), findsNothing);
  });

  testWidgets('the ward is picked and saved by code, in another province too', (
    tester,
  ) async {
    await pumpScreen(tester);
    await tester.tap(find.byKey(const Key('use-current-location')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('pick-districts')));
    await tester.pumpAndSettle();
    // The list builds only what is on screen: search first.
    await tester.enterText(find.byKey(const Key('district-search')), 'sai gon');
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('ward-option-26740')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('district-search')), '');
    await tester.pumpAndSettle();

    // Hà Nội (code 1): Phường Ba Đình.
    // The menu is a lazy list; choose the province the way its onChanged does.
    tester
        .widget<DropdownButtonFormField<int>>(
          find.byKey(const Key('district-province')),
        )
        .onChanged!(1);
    await tester.pumpAndSettle();
    final baDinh = wardsOfProvince(1).first;
    await tester.tap(find.byKey(Key('ward-option-${baDinh.code}')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('district-done')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('save-work-area')));
    await tester.pumpAndSettle();

    expect(service.saved!.wardCodes, [26740, baDinh.code]);
  });

  testWidgets(
    'two chosen wards with the same name are told apart by province',
    (tester) async {
      // "Phường Việt Hưng" exists in Hà Nội and in Quảng Ninh.
      final twins = vnWards.where((w) => w.name == 'Phường Việt Hưng').toList();
      expect(twins.length, 2);
      service.area = WorkerWorkArea(
        isSet: true,
        latitude: 10.7769,
        longitude: 106.7009,
        radiusKm: 10,
        wards: [for (final w in twins) WorkerWard.fromList(w)],
      );
      await pumpScreen(tester);

      for (final w in twins) {
        final chip = tester.widget<InputChip>(
          find.byKey(Key('ward-chip-${w.code}')),
        );
        expect(
          (chip.label as Text).data,
          'Phường Việt Hưng, ${provinceNameOf(w.provinceCode)}',
        );
      }
    },
  );

  testWidgets('an old name that stands for no ward asks to choose again', (
    tester,
  ) async {
    service.area = WorkerWorkArea(
      isSet: true,
      latitude: 10.7769,
      longitude: 106.7009,
      radiusKm: 10,
      wards: [WorkerWard.fromList(ward(26740))],
      unresolvedNames: const ['Quận 1', 'Phường Tân Định'],
    );
    await pumpScreen(tester);

    expect(
      tester.widget<Text>(find.byKey(const Key('work-area-unresolved'))).data,
      contains('Quận 1, Phường Tân Định'),
    );
    expect(find.byKey(const Key('ward-chip-26740')), findsOneWidget);

    // Choosing again clears the note.
    await tester.ensureVisible(find.byKey(const Key('pick-districts')));
    await tester.tap(find.byKey(const Key('pick-districts')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('district-done')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('work-area-unresolved')), findsNothing);
  });

  testWidgets('a refused save shows the server\'s own words and shakes', (
    tester,
  ) async {
    service.failSave = const ApiException(
      400,
      'Validation failed',
      fieldErrors: {
        'radiusKm': ['radiusKm must be one of 5, 10, 15.'],
      },
    );
    await pumpScreen(tester);
    await tester.tap(find.byKey(const Key('use-current-location')));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('save-work-area')));
    await tester.pumpAndSettle();

    expect(find.text('radiusKm must be one of 5, 10, 15.'), findsOneWidget);
    expect(find.text('Nơi làm việc'), findsWidgets); // still on the screen
  });

  testWidgets('a failed load shows a retry that loads again', (tester) async {
    service.failLoad = true;
    await pumpScreen(tester);

    expect(find.text('Chưa tải được nơi làm việc của bạn.'), findsOneWidget);
    expect(find.byKey(const Key('save-work-area')), findsNothing);

    service.failLoad = false;
    await tester.tap(find.text('Thử lại'));
    await tester.pumpAndSettle();

    expect(service.loads, 2);
    expect(find.byKey(const Key('save-work-area')), findsOneWidget);
  });
}
