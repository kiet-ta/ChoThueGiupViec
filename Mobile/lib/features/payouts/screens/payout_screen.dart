import 'package:flutter/material.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/eyebrow_badge.dart';
import '../../../core/widgets/nordic_card.dart';

/// Screen for Worker Payouts & Earnings (Slot M6).
class PayoutScreen extends StatelessWidget {
  const PayoutScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(
        title: const Text('Thu Nhập & Quyết Toán'),
      ),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: ListView(
            padding: const EdgeInsets.symmetric(horizontal: 16.0, vertical: 16.0),
            children: const [
              EyebrowBadge(text: 'Thu Nhập & Đối Soát Minh Bạch'),
              SizedBox(height: 12.0),
              Text(
                'Theo dõi thu nhập ca làm việc',
                style: NordicTypography.h2,
              ),
              SizedBox(height: 16.0),
              NordicCard(
                child: Padding(
                  padding: EdgeInsets.all(16.0),
                  child: Text(
                    'Xem số dư thu nhập sau hoa hồng (80% Freelancer), lịch sử các đợt giải ngân theo contract Payouts (M6).',
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
