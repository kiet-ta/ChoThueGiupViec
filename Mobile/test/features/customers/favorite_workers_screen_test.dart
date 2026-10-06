import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/customers/screens/favorite_workers_screen.dart';
import 'package:mobile/features/customers/services/favorite_worker_service.dart';

void main() {
  group('FavoriteWorkersScreen Widget Tests', () {
    late FavoriteWorkerService service;

    setUp(() {
      service = FavoriteWorkerService(isMock: true);
    });

    testWidgets('renders favorite workers list, rating, and CTA buttons', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: FavoriteWorkersScreen(service: service),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Thợ Quen Yêu Thích'), findsOneWidget);
      expect(find.text('Thợ quen của bạn'), findsOneWidget);
      expect(find.text('Lê Thị Cúc'), findsOneWidget);
      expect(find.text('Đặt Ca Với Thợ'), findsWidgets);
      expect(find.textContaining('ca hoàn thành'), findsWidgets);
    });

    testWidgets('displays empty state when no favorite workers exist', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      service.clearMockStore();

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: FavoriteWorkersScreen(service: service),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Chưa Có Thợ Quen Nào'), findsOneWidget);
    });

    testWidgets('removes worker, shows undo snackbar, and restores on undo', (tester) async {
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      await tester.pumpWidget(
        MaterialApp(
          theme: NordicTheme.lightTheme,
          home: FavoriteWorkersScreen(service: service),
        ),
      );
      await tester.pumpAndSettle();

      // Find remove heart button
      final removeButtons = find.byTooltip('Bỏ yêu thích');
      expect(removeButtons, findsWidgets);

      await tester.tap(removeButtons.first);
      await tester.pumpAndSettle();

      // Check snackbar with undo
      expect(find.textContaining('Đã bỏ yêu thích'), findsOneWidget);
      expect(find.text('Hoàn tác'), findsOneWidget);

      // Tap undo
      await tester.tap(find.text('Hoàn tác'));
      await tester.pumpAndSettle();

      final list = await service.getFavoriteWorkers();
      expect(list.length, greaterThanOrEqualTo(3));
    });
  });
}
