import 'package:flutter/widgets.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/widgets/status_pill.dart';
import 'package:mobile/features/booking/models/booking_models.dart';
import 'package:mobile/features/booking/screens/order_status_pill.dart';

/// Owner rule (2026-10-30): done is green, every step in between is amber, cancelled is red.
void main() {
  Future<PillTone> toneOf(WidgetTester tester, String status) async {
    await tester.pumpWidget(
      Directionality(
        textDirection: TextDirection.ltr,
        child: OrderStatusPill(status),
      ),
    );
    return tester.widget<StatusPill>(find.byType(StatusPill)).tone;
  }

  testWidgets('completed is green', (tester) async {
    expect(await toneOf(tester, OrderStatus.completed), PillTone.ok);
  });

  testWidgets('every step in between is amber', (tester) async {
    for (final status in [
      OrderStatus.pendingPayment,
      OrderStatus.paid,
      OrderStatus.dispatching,
      OrderStatus.awaitingFallback,
      OrderStatus.assigned,
    ]) {
      expect(await toneOf(tester, status), PillTone.warn, reason: status);
    }
  });

  testWidgets('cancelled is red', (tester) async {
    expect(await toneOf(tester, OrderStatus.cancelled), PillTone.bad);
  });
}
