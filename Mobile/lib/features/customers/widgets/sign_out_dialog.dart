import 'package:flutter/material.dart';

import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';

/// What the profile screen returns when the customer signed out there: the Tài khoản tab then signs out the
/// same way as its own row does.
const String customerSignedOutResult = 'signed-out';

/// The one sign-out question of the customer screens (Tài khoản tab and Hồ sơ); true when the customer confirmed.
Future<bool> confirmCustomerSignOut(BuildContext context) async {
  final confirm = await showDialog<bool>(
    context: context,
    builder: (ctx) => AlertDialog(
      title: const Text('Đăng xuất?', style: NordicTypography.h3),
      content: const Text(
        'Lần sau bạn cần mã xác nhận để vào lại.',
        style: NordicTypography.bodyRegular,
      ),
      actions: [
        TextButton(
          key: const Key('sign-out-cancel'),
          onPressed: () => Navigator.pop(ctx, false),
          child: const Text(
            'Huỷ',
            style: TextStyle(color: NordicColors.textSecondary),
          ),
        ),
        ElevatedButton(
          key: const Key('sign-out-confirm'),
          style: ElevatedButton.styleFrom(backgroundColor: NordicColors.error),
          onPressed: () => Navigator.pop(ctx, true),
          child: const Text('Đăng xuất', style: TextStyle(color: Colors.white)),
        ),
      ],
    ),
  );
  return confirm == true;
}
