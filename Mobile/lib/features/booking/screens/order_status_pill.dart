import 'package:flutter/widgets.dart';

import '../../../core/widgets/status_pill.dart';
import '../logic/order_logic.dart';
import '../models/booking_models.dart';

/// The order status as a pill (ui-templates `.pill`): the label from `orderStatusLabel`, the tone only supports it.
class OrderStatusPill extends StatelessWidget {
  final String status;

  const OrderStatusPill(this.status, {super.key});

  @override
  Widget build(BuildContext context) => StatusPill(
    orderStatusLabel(status),
    tone: switch (status) {
      OrderStatus.completed || OrderStatus.assigned => PillTone.ok,
      OrderStatus.pendingPayment ||
      OrderStatus.awaitingFallback => PillTone.warn,
      OrderStatus.cancelled => PillTone.bad,
      _ => PillTone.neutral,
    },
  );
}
