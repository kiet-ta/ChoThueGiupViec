import 'package:flutter/material.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/eyebrow_badge.dart';
import '../../../core/widgets/nordic_card.dart';

/// Screen for Agency Shift Roster & Profile (Slot M5).
class AgencyScreen extends StatelessWidget {
  const AgencyScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(
        title: const Text('Doanh Nghiệp / Agency'),
      ),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: ListView(
            padding: const EdgeInsets.symmetric(horizontal: 16.0, vertical: 16.0),
            children: const [
              EyebrowBadge(text: 'Đối Tác B2B & Quản Lý Ca'),
              SizedBox(height: 12.0),
              Text(
                'Lịch ca thợ Agency & Shift Roster',
                style: NordicTypography.h2,
              ),
              SizedBox(height: 16.0),
              NordicCard(
                child: Padding(
                  padding: EdgeInsets.all(16.0),
                  child: Text(
                    'Xem lịch trực ca của đội ngũ thợ thuộc Agency đối tác theo contract Agencies (M5).',
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
