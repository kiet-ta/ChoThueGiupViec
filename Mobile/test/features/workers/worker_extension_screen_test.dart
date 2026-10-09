import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/workers/models/worker_extension_models.dart';
import 'package:mobile/features/workers/screens/worker_extension_screen.dart';

void main() {
  group('WorkerExtensionScreen Tests (MOB-M4-06)', () {
    testWidgets('Renders offer details: customer name, work zone, extra hours, amount, buttons', (tester) async {
      await tester.pumpWidget(
        const MaterialApp(
          home: WorkerExtensionScreen(
            offer: WorkerExtensionOffer(
              assignmentId: 2001,
              extensionId: 301,
              customerName: 'Nguyễn Văn An',
              workZone: 'Phòng khách & Bếp - Căn hộ 402',
              extraHours: 2,
              extraAmount: 130000,
              currentEndTime: '17:00',
              newEndTime: '19:00',
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Làm Lần 2 #2001'), findsOneWidget);
      expect(find.text('Yêu Cầu Làm Lần 2 (BR-08)'), findsOneWidget);
      expect(find.text('Khách Hàng Yêu Cầu Làm Thêm Giờ'), findsOneWidget);
      expect(find.text('Nguyễn Văn An'), findsOneWidget);
      expect(find.text('Phòng khách & Bếp - Căn hộ 402'), findsOneWidget);
      expect(find.text('2 giờ'), findsOneWidget);
      expect(find.text('17:00 ➔ 19:00'), findsOneWidget);
      expect(find.text('+130.000 đ'), findsOneWidget);
      expect(find.text('Đồng Ý Nối Ca (+130.000 đ)'), findsOneWidget);
      expect(find.text('Từ Chối Làm Thêm'), findsOneWidget);
    });

    testWidgets('Tapping Accept button updates UI to ACCEPTED status with new end time', (tester) async {
      WorkerExtensionResponseDto? resultResponse;

      await tester.pumpWidget(
        MaterialApp(
          home: WorkerExtensionScreen(
            offer: const WorkerExtensionOffer(
              assignmentId: 2001,
              extensionId: 301,
              customerName: 'Nguyễn Văn An',
              workZone: 'Phòng khách',
              extraHours: 2,
              extraAmount: 130000,
              currentEndTime: '17:00',
              newEndTime: '19:00',
            ),
            onResponded: (res) {
              resultResponse = res;
            },
          ),
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.text('Đồng Ý Nối Ca (+130.000 đ)'));
      await tester.pumpAndSettle();

      expect(find.text('Đã Đồng Ý Nối Ca!'), findsOneWidget);
      expect(find.text('ACCEPTED'), findsOneWidget);
      expect(find.text('19:00'), findsOneWidget);
      expect(resultResponse?.status, 'ACCEPTED');
    });

    testWidgets('Tapping Decline button updates UI to DECLINED status', (tester) async {
      WorkerExtensionResponseDto? resultResponse;

      await tester.pumpWidget(
        MaterialApp(
          home: WorkerExtensionScreen(
            offer: const WorkerExtensionOffer(
              assignmentId: 2001,
              extensionId: 301,
              customerName: 'Nguyễn Văn An',
              workZone: 'Phòng khách',
              extraHours: 2,
              extraAmount: 130000,
              currentEndTime: '17:00',
              newEndTime: '19:00',
            ),
            onResponded: (res) {
              resultResponse = res;
            },
          ),
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.text('Từ Chối Làm Thêm'));
      await tester.pumpAndSettle();

      expect(find.text('Đã Từ Chối Yêu Cầu'), findsOneWidget);
      expect(find.text('DECLINED'), findsOneWidget);
      expect(resultResponse?.status, 'DECLINED');
    });
  });
}
