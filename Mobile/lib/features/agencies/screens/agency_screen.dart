import 'package:flutter/material.dart';

import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_page.dart';

/// Screen for the agency's shift roster and profile (Slot M5).
/// MOB-UI-23 (`Mobile/DESIGN-SYSTEM-WORKER.md`): `NordicPage.pushed`, tokens only.
class AgencyScreen extends StatelessWidget {
  const AgencyScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return NordicPage.pushed(
      title: 'Doanh nghiệp',
      children: const [
        Text('Lịch ca của đội thợ', style: NordicTypography.h2),
        SizedBox(height: 16.0),
        NordicCard(
          child: Text(
            'Xem lịch ca của đội thợ thuộc doanh nghiệp đối tác.',
            style: NordicTypography.bodyRegular,
          ),
        ),
      ],
    );
  }
}
