import 'package:flutter/material.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/eyebrow_badge.dart';
import '../../../core/widgets/nordic_card.dart';

/// Screen for Payments Flow (Slot M2).
class PaymentScreen extends StatelessWidget {
  const PaymentScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(
        title: const Text('Thanh Toán Ca Làm'),
      ),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: ListView(
            padding: const EdgeInsets.symmetric(horizontal: 16.0, vertical: 16.0),
            children: const [
              EyebrowBadge(text: 'Cổng Thanh Toán MoMo Sandbox'),
              SizedBox(height: 12.0),
              Text(
                'Thanh toán an toàn & minh bạch',
                style: NordicTypography.h2,
              ),
              SizedBox(height: 16.0),
              NordicCard(
                child: Padding(
                  padding: EdgeInsets.all(16.0),
                  child: Text(
                    'SANDBOX - Không thu tiền thật. Quét mã QR MoMo Sandbox theo contract Payments (M2).',
                    style: NordicTypography.bodyRegular,
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
