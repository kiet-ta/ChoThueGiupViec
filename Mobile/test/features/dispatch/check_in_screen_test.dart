import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/device/camera_wrapper.dart';
import 'package:mobile/core/device/location_wrapper.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/dispatch/models/check_in_result.dart';
import 'package:mobile/features/dispatch/screens/check_in_screen.dart';
import 'package:mobile/features/dispatch/services/dispatch_service.dart';

void main() {
  group('CheckInScreen Widget Tests', () {
    late DispatchService service;
    late LocationWrapper locationWrapper;
    late CameraWrapper cameraWrapper;

    setUp(() {
      locationWrapper = LocationWrapper(
        mockLocation: GeoPoint(
          latitude: 21.028511,
          longitude: 105.854167,
          timestamp: DateTime.now(),
        ),
      );
      cameraWrapper = CameraWrapper(
        mockImage: CapturedImage(
          path: '/mock/photos/plate_1001.jpg',
          capturedAt: DateTime.now(),
        ),
      );
      service = DispatchService(
        locationWrapper: locationWrapper,
        cameraWrapper: cameraWrapper,
        isMock: true,
      );
    });

    Widget createScreen() {
      return MaterialApp(
        theme: NordicTheme.lightTheme,
        home: CheckInScreen(
          assignmentId: 1001,
          destinationLatitude: 21.028511,
          destinationLongitude: 105.854167,
          destinationAddress: '123 Đường Cầu Giấy, Hà Nội',
          dispatchService: service,
        ),
      );
    }

    testWidgets('renders check in screen with destination address and GPS button', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      await tester.pumpWidget(createScreen());
      await tester.pumpAndSettle();

      expect(find.text('Check-in Hiện Trường'), findsOneWidget);
      expect(find.text('Đã đến địa chỉ ca làm việc?'), findsOneWidget);
      expect(find.text('123 Đường Cầu Giấy, Hà Nội'), findsOneWidget);
      expect(find.byKey(const Key('check_in_gps_button')), findsOneWidget);
    });

    testWidgets('successful GPS check-in (distance <= 100m) shows success view', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      await tester.pumpWidget(createScreen());
      await tester.pumpAndSettle();

      // Tap GPS check in
      await tester.tap(find.byKey(const Key('check_in_gps_button')));
      await tester.pumpAndSettle();

      expect(find.text('Đã Đến Nơi Thành Công'), findsOneWidget);
      expect(find.text('Check-in thành công bằng định vị GPS!'), findsOneWidget);
      expect(find.byKey(const Key('continue_to_job_button')), findsOneWidget);
    });

    testWidgets('GPS check-in deviating > 100m presents alternative verification methods', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      // Inject mock result with distance > 100m
      service.setMockCheckInResult(
        CheckInResult(
          checkInId: 2002,
          assignmentId: 1001,
          checkedInAt: DateTime.now(),
          distanceMeters: 250.0,
          isGpsVerified: false,
          requiresAlternativeVerification: true,
          verificationMethod: 'GPS',
          isCustomerConfirmed: false,
        ),
      );

      await tester.pumpWidget(createScreen());
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('check_in_gps_button')));
      await tester.pumpAndSettle();

      expect(find.text('Vị trí chưa khớp'), findsOneWidget);
      expect(find.textContaining('250 mét'), findsOneWidget);
      expect(find.byKey(const Key('take_plate_photo_button')), findsOneWidget);
      expect(find.byKey(const Key('request_customer_confirmation_button')), findsOneWidget);

      // Verify taking plate photo fallback
      await tester.tap(find.byKey(const Key('take_plate_photo_button')));
      await tester.pumpAndSettle();

      expect(find.text('Đã Đến Nơi Thành Công'), findsOneWidget);
      expect(find.text('Check-in thành công bằng ảnh chụp biển số nhà!'), findsOneWidget);
    });
  });
}
