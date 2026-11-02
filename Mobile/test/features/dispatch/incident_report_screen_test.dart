import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/device/camera_wrapper.dart';
import 'package:mobile/core/device/location_wrapper.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/dispatch/screens/incident_report_screen.dart';
import 'package:mobile/features/dispatch/services/dispatch_service.dart';

void main() {
  group('IncidentReportScreen Widget Tests', () {
    late DispatchService service;
    late LocationWrapper locationWrapper;
    late CameraWrapper cameraWrapper;

    setUp(() {
      locationWrapper = LocationWrapper(
        mockLocation: GeoPoint(
          latitude: 10.776889,
          longitude: 106.700806,
          timestamp: DateTime.now(),
        ),
      );
      cameraWrapper = CameraWrapper(
        mockImage: CapturedImage(
          path: '/mock/path/incident_evidence.jpg',
          capturedAt: DateTime.now(),
        ),
      );
      service = DispatchService(
        isMock: true,
        cameraWrapper: cameraWrapper,
        locationWrapper: locationWrapper,
      );
    });

    Widget createScreen() {
      return MaterialApp(
        theme: NordicTheme.lightTheme,
        home: IncidentReportScreen(
          assignmentId: 1001,
          dispatchService: service,
        ),
      );
    }

    testWidgets(
      'Renders form and validates requirement of photo and description',
      (WidgetTester tester) async {
        tester.view.physicalSize = const Size(390, 844);
        tester.view.devicePixelRatio = 1.0;
        addTearDown(() {
          tester.view.resetPhysicalSize();
          tester.view.resetDevicePixelRatio();
        });

        // Clear mock image for first test to verify validation
        final emptyCamService = DispatchService(
          isMock: true,
          cameraWrapper: CameraWrapper(mockImage: null),
          locationWrapper: locationWrapper,
        );

        await tester.pumpWidget(
          MaterialApp(
            theme: NordicTheme.lightTheme,
            home: IncidentReportScreen(
              assignmentId: 1001,
              dispatchService: emptyCamService,
            ),
          ),
        );
        await tester.pumpAndSettle();

        // Verify title & disclaimer
        expect(find.text('Báo sự cố'), findsOneWidget);

        // Try tapping submit without capturing photo
        final submitButton = find.byKey(const Key('submit_incident_button'));
        await tester.scrollUntilVisible(
          submitButton,
          200,
          scrollable: find.byType(Scrollable).first,
        );
        expect(submitButton, findsOneWidget);
        await tester.ensureVisible(submitButton);
        await tester.pumpAndSettle();
        await tester.tap(submitButton);
        await tester.pumpAndSettle();

        // Expect validation error for photo evidence
        expect(
          find.textContaining('Hãy chụp ảnh bằng chứng', skipOffstage: false),
          findsOneWidget,
        );
      },
    );

    testWidgets(
      'Can capture photo, input description, and submit incident successfully',
      (WidgetTester tester) async {
        tester.view.physicalSize = const Size(390, 844);
        tester.view.devicePixelRatio = 1.0;
        addTearDown(() {
          tester.view.resetPhysicalSize();
          tester.view.resetDevicePixelRatio();
        });

        await tester.pumpWidget(createScreen());
        await tester.pumpAndSettle();

        // Capture photo
        final captureBtn = find.byKey(const Key('take_incident_photo_button'));
        expect(captureBtn, findsOneWidget);
        await tester.tap(captureBtn);
        await tester.pumpAndSettle();

        expect(
          find.textContaining('Đã chụp: /mock/path/incident_evidence.jpg'),
          findsOneWidget,
        );

        // Input description
        final inputField = find.byKey(const Key('incident_description_input'));
        expect(inputField, findsOneWidget);
        await tester.enterText(
          inputField,
          'Xe máy bị nổ lốp giữa đường cầu Sài Gòn',
        );
        await tester.pumpAndSettle();

        // Submit incident
        await tester.drag(find.byType(ListView), const Offset(0, -300));
        await tester.pumpAndSettle();

        final submitButton = find.byKey(const Key('submit_incident_button'));
        await tester.ensureVisible(submitButton);
        await tester.pumpAndSettle();
        await tester.tap(submitButton);
        await tester.pumpAndSettle();

        // Check success state UI
        expect(find.text('Đã ghi nhận sự cố'), findsOneWidget);
        expect(
          find.textContaining('miễn phạt theo chính sách'),
          findsOneWidget,
        );
        expect(find.text('Đang tìm thợ'), findsOneWidget);
      },
    );
  });
}
