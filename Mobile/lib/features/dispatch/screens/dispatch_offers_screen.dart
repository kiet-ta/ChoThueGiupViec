import 'package:flutter/material.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/eyebrow_badge.dart';
import '../../../core/widgets/nordic_card.dart';

/// Screen for Dispatch & Offers (Slot M3).
class DispatchOffersScreen extends StatelessWidget {
  const DispatchOffersScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(
        title: const Text('Bàn Điều Phối Ca'),
      ),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: ListView(
            padding: const EdgeInsets.symmetric(horizontal: 16.0, vertical: 16.0),
            children: const [
              EyebrowBadge(text: 'Điều Phối Ca & Hiện Trường'),
              SizedBox(height: 12.0),
              Text(
                'Nhận cuốc 30 giây & Check-in',
                style: NordicTypography.h2,
              ),
              SizedBox(height: 16.0),
              NordicCard(
                child: Padding(
                  padding: EdgeInsets.all(16.0),
                  child: Text(
                    'Khu vực nhận ca làm việc 30s, đếm ngược thời gian, GPS check-in (≤100m) và báo sự cố theo contract Dispatch (M3).',
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
