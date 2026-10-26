import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/app/app.dart';

void main() {
  testWidgets('ToAmApp boots up and shows the minimal welcome', (tester) async {
    // Target 390x844 mobile design frame
    tester.view.physicalSize = const Size(390, 844);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(() {
      tester.view.resetPhysicalSize();
      tester.view.resetDevicePixelRatio();
    });

    await tester.pumpWidget(const ToAmApp());
    await tester.pumpAndSettle();

    // The minimal welcome of ui-templates `shared/welcome.html` (MOB-UI-02).
    expect(find.text('TỔ ẤM'), findsOneWidget);
    expect(find.text('Nhà sạch, lòng nhẹ.'), findsOneWidget);
    expect(find.text('Tôi cần giúp việc'), findsOneWidget);
    expect(find.text('Tôi là người giúp việc'), findsOneWidget);

    // No landing page claims any more (README "Why").
    expect(find.text('Bảo hiểm an tâm 50 triệu'), findsNothing);
  });
}
