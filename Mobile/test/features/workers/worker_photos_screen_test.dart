import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/workers/screens/worker_photos_screen.dart';

void main() {
  group('WorkerPhotosScreen Tests', () {
    testWidgets('Renders header, phase selector, angle selector, camera guide, and submit button', (tester) async {
      await tester.pumpWidget(
        const MaterialApp(
          home: WorkerPhotosScreen(
            assignmentId: 2001,
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Ảnh Before/After #2001'), findsOneWidget);
      expect(find.text('Kiểm Tra Chất Lượng VoL'), findsOneWidget);
      expect(find.text('BEFORE (Trước)'), findsOneWidget);
      expect(find.text('AFTER (Sau)'), findsOneWidget);
      expect(find.text('Góc 1'), findsOneWidget);
      expect(find.text('Góc 1: Toàn cảnh'), findsOneWidget);
      expect(find.text('Gửi Ảnh Chụp'), findsOneWidget);
    });

    testWidgets('Switching angles updates camera guide text', (tester) async {
      await tester.pumpWidget(
        const MaterialApp(
          home: WorkerPhotosScreen(
            assignmentId: 2001,
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Góc 1: Toàn cảnh'), findsOneWidget);

      await tester.tap(find.text('Góc 2'));
      await tester.pumpAndSettle();

      expect(find.text('Góc 2: Bàn ghế & tủ'), findsOneWidget);
    });

    testWidgets('Uploading blurred image triggers VoL threshold alert and retake prompt', (tester) async {
      await tester.pumpWidget(
        const MaterialApp(
          home: WorkerPhotosScreen(
            assignmentId: 2001,
            volThreshold: 100.0,
          ),
        ),
      );
      await tester.pumpAndSettle();

      // Tap test retake button which sets blurry URL
      await tester.tap(find.text('Tải Ảnh Mờ (Test retake)'));
      await tester.pumpAndSettle();

      // Tap submit
      await tester.tap(find.text('Gửi Ảnh Chụp'));
      await tester.pumpAndSettle();

      expect(find.textContaining('Ảnh bị mờ'), findsOneWidget);
      expect(find.textContaining('100.0'), findsOneWidget);
    });

    testWidgets('Uploading sharp image passes VoL check and shows success prompt', (tester) async {
      await tester.pumpWidget(
        const MaterialApp(
          home: WorkerPhotosScreen(
            assignmentId: 2001,
            volThreshold: 100.0,
          ),
        ),
      );
      await tester.pumpAndSettle();

      // Tap submit with default sharp URL
      await tester.tap(find.text('Gửi Ảnh Chụp'));
      await tester.pumpAndSettle();

      expect(find.textContaining('Tải ảnh thành công'), findsOneWidget);
      expect(find.textContaining('ĐẠT CHUẨN'), findsOneWidget);
    });
  });
}
