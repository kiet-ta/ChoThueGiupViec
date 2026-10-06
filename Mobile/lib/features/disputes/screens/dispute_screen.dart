import 'package:flutter/material.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/eyebrow_badge.dart';
import '../../../core/widgets/nordic_card.dart';

/// Screen for Disputes & Incident Claims (Slot M6).
class DisputeScreen extends StatelessWidget {
  const DisputeScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(
        title: const Text('Khiếu Nại & Tranh Chấp'),
      ),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: ListView(
            padding: const EdgeInsets.symmetric(horizontal: 16.0, vertical: 16.0),
            children: const [
              EyebrowBadge(text: 'Bảo Vệ Quyền Lợi & Hòa Giải'),
              SizedBox(height: 12.0),
              Text(
                'Giải quyết sự vụ minh bạch',
                style: NordicTypography.h2,
              ),
              SizedBox(height: 16.0),
              NordicCard(
                child: Padding(
                  padding: EdgeInsets.all(16.0),
                  child: Text(
                    'Gửi khiếu nại ca làm, đính kèm bằng chứng ảnh và theo dõi tiến độ xử lý theo contract Disputes (M6).',
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
