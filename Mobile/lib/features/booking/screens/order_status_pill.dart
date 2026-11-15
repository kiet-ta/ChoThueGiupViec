import 'package:flutter/widgets.dart';

import '../../../core/widgets/status_pill.dart';
import '../logic/order_logic.dart';
import '../models/booking_models.dart';

/// The order status as a pill (ui-templates `.pill`): the label from `orderStatusLabel`, the tone only supports it.
class OrderStatusPill extends StatelessWidget {
  final String status;
  final Key? textKey;

  /// The text, when the screen knows a finer step than the order status (ORDER-STATUS-SYNC-01).
  final String? label;

  const OrderStatusPill(this.status, {super.key, this.textKey, this.label});

  @override
  Widget build(BuildContext context) => StatusPill(
    label ?? orderStatusLabel(status),
    textKey: textKey,
    // Owner rule (2026-10-30): done is green, every step in between is amber, cancelled is red.
    tone: switch (status) {
      OrderStatus.completed => PillTone.ok,
      OrderStatus.cancelled => PillTone.bad,
      _ => PillTone.warn,
    },
  );
}
