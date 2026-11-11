import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/device/device_location.dart';
import 'package:mobile/core/device/location_wrapper.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/network/api_response.dart';
import 'package:mobile/features/workers/models/worker_assignment_models.dart';
import 'package:mobile/features/workers/models/worker_route_models.dart';
import 'package:mobile/features/workers/screens/worker_route_screen.dart';
import 'package:mobile/features/workers/services/workers_assignments_service.dart';
import 'package:mobile/features/workers/services/workers_route_service.dart';

import 'design_frame.dart';

class _Job extends WorkersAssignmentsService {
  final double lat;
  final double lng;
  final ApiException? failure;

  _Job({this.lat = 10.8031, this.lng = 106.7147, this.failure})
    : super(apiClient: ApiClient(baseUrl: 'http://api.test'));

  @override
  Future<ApiResponse<WorkerAssignmentDetailDto>> getAssignment(
    int assignmentId,
  ) async {
    if (failure != null) throw failure!;
    return ApiResponse<WorkerAssignmentDetailDto>(
      success: true,
      message: 'OK',
      data: WorkerAssignmentDetailDto(
        assignmentId: assignmentId,
        orderId: 1,
        orderCode: 'ORD-1',
        slotDate: '2026-11-11',
        shiftCode: 'SHIFT_MORNING',
        startTime: '08:00',
        endTime: '12:00',
        status: 'ASSIGNED',
        serviceTier: 'ECONOMY',
        addressLine: '208 Nguyễn Hữu Cảnh',
        district: 'Bình Thạnh',
        city: 'TP. Hồ Chí Minh',
        latitude: lat,
        longitude: lng,
        payoutAmount: 221000,
      ),
    );
  }
}

class _Routes extends WorkersRouteService {
  final List<WorkerRouteOptionDto> options;
  final ApiException? failure;
  double? fromLat;
  double? fromLng;
  int calls = 0;

  _Routes({this.options = const [], this.failure})
    : super(apiClient: ApiClient(baseUrl: 'http://api.test'));

  @override
  Future<ApiResponse<WorkerRouteDto>> getRoutes(
    int assignmentId, {
    required double fromLat,
    required double fromLng,
  }) async {
    calls++;
    this.fromLat = fromLat;
    this.fromLng = fromLng;
    if (failure != null) throw failure!;
    return ApiResponse<WorkerRouteDto>(
      success: true,
      message: 'OK',
      data: WorkerRouteDto(
        assignmentId: assignmentId,
        destinationLatitude: 10.8031,
        destinationLongitude: 106.7147,
        routes: options,
      ),
    );
  }
}

class _Where implements IDeviceLocationProvider {
  final DeviceLocationFailure? failure;
  _Where({this.failure});

  @override
  Future<GeoPoint> currentPosition() async {
    if (failure != null) throw DeviceLocationException(failure!);
    return GeoPoint(
      latitude: 10.7769,
      longitude: 106.7009,
      timestamp: DateTime.utc(2026, 11, 11),
    );
  }
}

class _Launcher implements IMapLauncher {
  final bool result;
  final List<Uri> opened = [];
  _Launcher({this.result = true});

  @override
  Future<bool> open(Uri uri) async {
    opened.add(uri);
    return result;
  }
}

const _fast = WorkerRouteOptionDto(
  distanceMeters: 5255,
  durationSeconds: 805.8,
  polyline: 'a',
);
const _mid = WorkerRouteOptionDto(
  distanceMeters: 5882,
  durationSeconds: 845.8,
  polyline: 'b',
);
const _slow = WorkerRouteOptionDto(
  distanceMeters: 6442,
  durationSeconds: 991.8,
  polyline: 'c',
);

Future<_Launcher> _pump(
  WidgetTester tester, {
  _Job? job,
  _Routes? routes,
  IDeviceLocationProvider? where,
  _Launcher? launcher,
}) async {
  useDesignFrame(tester);
  final l = launcher ?? _Launcher();
  await tester.pumpWidget(
    MaterialApp(
      home: WorkerRouteScreen(
        assignmentId: 55,
        assignmentsService: job ?? _Job(),
        routeService: routes ?? _Routes(options: [_fast]),
        locationProvider: where ?? _Where(),
        mapLauncher: l,
      ),
    ),
  );
  await tester.pumpAndSettle();
  return l;
}

void main() {
  testWidgets('one route: shows distance, time and the fastest tag', (
    tester,
  ) async {
    final routes = _Routes(options: [_fast]);
    await _pump(tester, routes: routes);

    expect(
      find.text('208 Nguyễn Hữu Cảnh, Bình Thạnh, TP. Hồ Chí Minh'),
      findsOneWidget,
    );
    expect(find.text('Đường 1 · 5,3 km · ~13 phút'), findsOneWidget);
    expect(find.text('Nhanh nhất'), findsOneWidget);
    expect(find.byKey(const Key('route-option-1')), findsNothing);
    expect(routes.fromLat, 10.7769);
    expect(routes.fromLng, 106.7009);
  });

  testWidgets('three routes: all listed, the worker can pick another', (
    tester,
  ) async {
    await _pump(tester, routes: _Routes(options: [_fast, _mid, _slow]));

    expect(find.text('Đường 1 · 5,3 km · ~13 phút'), findsOneWidget);
    expect(find.text('Đường 2 · 5,9 km · ~14 phút'), findsOneWidget);
    expect(find.text('Đường 3 · 6,4 km · ~17 phút'), findsOneWidget);
    expect(find.text('Nhanh nhất'), findsOneWidget);
    expect(find.text('Đang chọn'), findsNothing);

    await tester.tap(find.byKey(const Key('route-option-2')));
    await tester.pumpAndSettle();
    expect(find.text('Đang chọn'), findsOneWidget);
    expect(
      find.text(
        'Ứng dụng bản đồ sẽ tự chọn đường đi. Thời gian ở đây là ước tính, chưa tính tắc đường.',
      ),
      findsOneWidget,
    );
  });

  testWidgets('two routes are listed', (tester) async {
    await _pump(tester, routes: _Routes(options: [_fast, _mid]));
    expect(find.byKey(const Key('route-option-0')), findsOneWidget);
    expect(find.byKey(const Key('route-option-1')), findsOneWidget);
    expect(find.byKey(const Key('route-option-2')), findsNothing);
  });

  testWidgets('Chỉ đường opens the map app at the house', (tester) async {
    final launcher = await _pump(tester);

    await tester.tap(find.byKey(const Key('route-navigate-button')));
    await tester.pumpAndSettle();

    expect(launcher.opened, hasLength(1));
    expect(launcher.opened.single.host, 'www.google.com');
    expect(
      launcher.opened.single.queryParameters['destination'],
      '10.8031,106.7147',
    );
    expect(launcher.opened.single.queryParameters['travelmode'], 'two-wheeler');
  });

  testWidgets('no map app: a message, not a crash', (tester) async {
    await _pump(tester, launcher: _Launcher(result: false));

    await tester.tap(find.byKey(const Key('route-navigate-button')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('route-launch-error')), findsOneWidget);
  });

  testWidgets('routing engine down (502): message, Chỉ đường still works', (
    tester,
  ) async {
    final launcher = await _pump(
      tester,
      routes: _Routes(failure: const ApiException(502, 'bad gateway')),
    );

    expect(
      find.text('Chưa tính được lộ trình. Bạn vẫn có thể bấm Chỉ đường.'),
      findsOneWidget,
    );
    expect(find.byKey(const Key('route-option-0')), findsNothing);

    await tester.tap(find.byKey(const Key('route-navigate-button')));
    await tester.pumpAndSettle();
    expect(launcher.opened, hasLength(1));
  });

  testWidgets('location refused: Vietnamese message, Chỉ đường still works', (
    tester,
  ) async {
    final routes = _Routes(options: [_fast]);
    final launcher = await _pump(
      tester,
      routes: routes,
      where: _Where(failure: DeviceLocationFailure.permissionDenied),
    );

    expect(
      find.text(
        deviceLocationFailureText(DeviceLocationFailure.permissionDenied),
      ),
      findsOneWidget,
    );
    expect(routes.calls, 0);
    await tester.tap(find.byKey(const Key('route-navigate-button')));
    await tester.pumpAndSettle();
    expect(launcher.opened, hasLength(1));
  });

  testWidgets(
    'zero coordinates: message, the map link uses the written address',
    (tester) async {
      final routes = _Routes(options: [_fast]);
      final launcher = await _pump(
        tester,
        job: _Job(lat: 0, lng: 0),
        routes: routes,
      );

      expect(
        find.text('Đơn này chưa có toạ độ nên chưa tính được lộ trình.'),
        findsOneWidget,
      );
      expect(routes.calls, 0);
      await tester.tap(find.byKey(const Key('route-navigate-button')));
      await tester.pumpAndSettle();
      expect(
        launcher.opened.single.queryParameters['destination'],
        '208 Nguyễn Hữu Cảnh, Bình Thạnh, TP. Hồ Chí Minh',
      );
    },
  );

  testWidgets('job not found (404): message and a retry, no map button', (
    tester,
  ) async {
    await _pump(
      tester,
      job: _Job(failure: const ApiException(404, 'not found')),
    );

    expect(find.byKey(const Key('route-job-error')), findsOneWidget);
    expect(find.byKey(const Key('route-navigate-button')), findsNothing);
    expect(find.byKey(const Key('route-retry-button')), findsOneWidget);
  });

  testWidgets('job already started (409): says so', (tester) async {
    await _pump(
      tester,
      routes: _Routes(failure: const ApiException(409, 'started')),
    );
    expect(
      find.text('Ca này đã bắt đầu nên không còn lộ trình.'),
      findsOneWidget,
    );
  });

  group('formatting', () {
    test('distance', () {
      expect(formatRouteDistance(850.4), '850 m');
      expect(formatRouteDistance(3200), '3,2 km');
      expect(formatRouteDistance(1000), '1,0 km');
    });

    test('duration', () {
      expect(formatRouteDuration(10), '~1 phút');
      expect(formatRouteDuration(720), '~12 phút');
      expect(formatRouteDuration(3900), '~1 giờ 5 phút');
      expect(formatRouteDuration(3600), '~1 giờ');
    });

    test('map link: point, address, nothing', () {
      expect(
        mapDirectionsUri(
          latitude: 10.8,
          longitude: 106.7,
          address: 'x',
        )!.queryParameters['destination'],
        '10.8,106.7',
      );
      expect(
        mapDirectionsUri(
          latitude: 0,
          longitude: 0,
          address: ' Nhà A ',
        )!.queryParameters['destination'],
        'Nhà A',
      );
      expect(mapDirectionsUri(latitude: 0, longitude: 0, address: ' '), isNull);
    });

    test('route json', () {
      final dto = WorkerRouteDto.fromJson({
        'assignmentId': 5,
        'destinationLatitude': 10.8,
        'destinationLongitude': 106.7,
        'routes': [
          {'distanceMeters': 5255, 'durationSeconds': 805.8, 'polyline': 'a'},
          {'distanceMeters': 5882.5, 'durationSeconds': 845.8, 'polyline': 'b'},
        ],
      });
      expect(dto.routes, hasLength(2));
      expect(dto.routes[1].distanceMeters, 5882.5);
      expect(WorkerRouteDto.fromJson({}).routes, isEmpty);
    });
  });
}
