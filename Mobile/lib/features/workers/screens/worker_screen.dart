import 'package:flutter/material.dart';

import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/avatar_initials.dart';
import '../../../core/widgets/grouped_list_card.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../agencies/routes.dart';
import '../../disputes/models/dispute_models.dart';
import '../../disputes/routes.dart';
import '../../disputes/screens/dispute_screen.dart';
import '../routes.dart';

/// The worker's Tài khoản tab and the `/workers/profile` screen: who is signed in
/// and the links to eKYC, the company schedule and disputes. The rating and criteria averages of the template need an API
/// that does not exist yet, so they are not shown.
/// MOB-UI-24 (`Mobile/DESIGN-SYSTEM-WORKER.md`): `NordicPage` (tab, or pushed when it has a back route), tokens only.
class WorkerScreen extends StatelessWidget {
  /// The worker's id from the login; there is no profile endpoint to read a name from.
  final int? workerId;

  /// Signs out; without it (the pushed screen) the sign-out row is not shown.
  final VoidCallback? onSignOut;

  const WorkerScreen({super.key, this.workerId, this.onSignOut});

  void _open(BuildContext context, String route, {Object? arguments}) {
    Navigator.of(context).pushNamed(route, arguments: arguments);
  }

  @override
  Widget build(BuildContext context) {
    final pushed = ModalRoute.of(context)?.canPop ?? false;
    final title = workerId == null ? 'Thợ giúp việc' : 'Thợ #$workerId';
    final children = [
      NordicCard(
        child: Row(
          children: [
            AvatarInitials(title, size: 72),
            const SizedBox(width: NordicSpace.sm),
            Expanded(
              child: Text(
                title,
                key: const Key('worker-account-name'),
                style: NordicTypography.titleMedium,
              ),
            ),
          ],
        ),
      ),
      const SizedBox(height: NordicSpace.lg),
      GroupedListCard(
        items: [
          GroupedListItem(
            key: const Key('worker-account-ekyc'),
            icon: Icons.badge_outlined,
            title: 'Xác minh danh tính',
            onTap: () => _open(context, WorkerRoutes.ekyc),
          ),
          GroupedListItem(
            key: const Key('worker-account-roster'),
            icon: Icons.groups_outlined,
            title: 'Lịch ca công ty',
            onTap: () => _open(context, AgencyRoutes.roster),
          ),
          GroupedListItem(
            key: const Key('worker-account-disputes'),
            icon: Icons.flag_outlined,
            title: 'Khiếu nại',
            onTap: () => _open(
              context,
              DisputeRoutes.disputes,
              arguments: const DisputeScreenArgs(role: DisputeRole.worker),
            ),
          ),
        ],
      ),
      if (onSignOut != null) ...[
        const SizedBox(height: NordicSpace.md),
        GroupedListCard(
          items: [
            GroupedListItem(
              key: const Key('home-sign-out'),
              icon: Icons.logout,
              title: 'Đăng xuất',
              danger: true,
              onTap: onSignOut,
            ),
          ],
        ),
      ],
    ];
    return pushed
        ? NordicPage.pushed(title: 'Hồ sơ và chất lượng', children: children)
        : NordicPage.tab(title: 'Tài khoản', children: children);
  }
}
