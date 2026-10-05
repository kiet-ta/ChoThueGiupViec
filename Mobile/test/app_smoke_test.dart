import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/app/app.dart';

void main() {
  testWidgets('ToAmApp boots up and displays Nordic Care home screen', (tester) async {
    // Target 390x844 mobile design frame
    tester.view.physicalSize = const Size(390, 844);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(() {
      tester.view.resetPhysicalSize();
      tester.view.resetDevicePixelRatio();
    });

    await tester.pumpWidget(const ToAmApp());
    await tester.pumpAndSettle();

    // Verify Brand title
    expect(find.text('TỔ ẤM'), findsOneWidget);
    expect(find.text('Nordic Care'), findsOneWidget);

    // Verify 4 Trust Badges
    expect(find.text('Thợ mang 100% đồ nghề'), findsOneWidget);
    expect(find.text('Nghiệm thu tại chỗ & dọn lại'), findsOneWidget);
    expect(find.text('Bảo hiểm an tâm 50 triệu'), findsOneWidget);
    expect(find.text('Minh bạch - Không phụ phí'), findsOneWidget);

    // Verify Customer mode is active by default
    expect(find.text('Căn Hộ Chung Cư'), findsOneWidget);
    expect(find.text('Đặt Ca Ngay'), findsOneWidget);

    // Tap on Worker mode toggle
    await tester.tap(find.text('Chuyên Viên / Thợ'));
    await tester.pumpAndSettle();

    // Verify Worker mode shows dispatch desk
    expect(find.text('Bàn Điều Phối Ca Làm'), findsOneWidget);
    expect(find.text('Vào Trực Ca'), findsOneWidget);
  });
}
