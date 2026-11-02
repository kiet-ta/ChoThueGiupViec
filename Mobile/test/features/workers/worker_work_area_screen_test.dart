import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/device/device_location.dart';
import 'package:mobile/core/device/location_wrapper.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/network/api_response.dart';
import 'package:mobile/features/customers/models/vn_admin_units.dart';
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
        districts: request.districts,
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
  final wards = vietnamAdminUnits['Thành phố Hồ Chí Minh']!;

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
      districts: [wards[0], wards[1]],
    );
    await pumpScreen(tester);

    expect(find.text('Nơi làm việc'), findsOneWidget);
    expect(find.text('Điểm làm việc: 10.7769, 106.7009'), findsOneWidget);
    expect(find.byKey(Key('district-chip-${wards[0]}')), findsOneWidget);
    expect(find.byKey(Key('district-chip-${wards[1]}')), findsOneWidget);
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
    await tester.tap(find.byKey(Key('district-option-${wards[0]}')));
    await tester.tap(find.byKey(Key('district-option-${wards[1]}')));
    await tester.pumpAndSettle();
    expect(find.text('Chọn phường/xã (2/10)'), findsOneWidget);
    await tester.tap(find.byKey(const Key('district-done')));
    await tester.pumpAndSettle();
    expect(find.byKey(Key('district-chip-${wards[0]}')), findsOneWidget);

    await tester.tap(find.byKey(const Key('save-work-area')));
    await tester.pumpAndSettle();

    expect(service.saved, isNotNull);
    expect(service.saved!.latitude, 10.7769);
    expect(service.saved!.longitude, 106.7009);
    expect(service.saved!.radiusKm, 5);
    expect(service.saved!.districts, [wards[0], wards[1]]);
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
    expect(service.saved!.districts, isEmpty);
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
        find.byKey(Key('district-option-${wards[i]}')),
      );
      await tester.tap(find.byKey(Key('district-option-${wards[i]}')));
      await tester.pump();
    }
    expect(find.text('Chọn phường/xã (10/10)'), findsOneWidget);

    await tester.ensureVisible(find.byKey(Key('district-option-${wards[10]}')));
    await tester.tap(find.byKey(Key('district-option-${wards[10]}')));
    await tester.pumpAndSettle();

    expect(find.text('Chọn tối đa 10 phường/xã.'), findsOneWidget);
    expect(find.text('Chọn phường/xã (10/10)'), findsOneWidget);
  });

  testWidgets('the search narrows the list of wards', (tester) async {
    await pumpScreen(tester);
    await tester.tap(find.byKey(const Key('pick-districts')));
    await tester.pumpAndSettle();
    expect(find.byKey(Key('district-option-${wards[0]}')), findsOneWidget);

    await tester.enterText(
      find.byKey(const Key('district-search')),
      'zzzzzz-no-ward',
    );
    await tester.pumpAndSettle();

    expect(find.byKey(Key('district-option-${wards[0]}')), findsNothing);
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
