import 'package:flutter/material.dart';

import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/avatar_initials.dart';
import '../../../core/widgets/grouped_list_card.dart';
import '../../agencies/routes.dart';
import '../../disputes/models/dispute_models.dart';
import '../../disputes/routes.dart';
import '../../disputes/screens/dispute_screen.dart';
import '../routes.dart';

/// The worker's Tài khoản tab (ui-templates `worker/account.html`) and the `/workers/profile` screen: who is signed in
/// and the links to eKYC, the company schedule and disputes. The rating and criteria averages of the template need an API
/// that does not exist yet, so they are not shown.
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
    return Scaffold(
      appBar: pushed ? AppBar(title: const Text('Hồ sơ & chất lượng')) : null,
      body: SafeArea(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 390.0),
            child: ListView(
              padding: const EdgeInsets.fromLTRB(20, 8, 20, 32),
              children: [
                if (!pushed)
                  const Padding(
                    padding: EdgeInsets.only(top: 8, bottom: 16),
                    child: Text(
                      'Tài khoản',
                      style: NordicTypography.screenTitle,
                    ),
                  ),
                Container(
                  padding: const EdgeInsets.all(16),
                  decoration: BoxDecoration(
                    color: NordicColors.surface,
                    borderRadius: BorderRadius.zero,
                    border: Border.all(color: NordicColors.border),
                  ),
                  child: Row(
                    children: [
                      AvatarInitials(title, size: 72),
                      const SizedBox(width: 12),
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
                const SizedBox(height: 24),
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
                        arguments: const DisputeScreenArgs(
                          role: DisputeRole.worker,
                        ),
                      ),
                    ),
                  ],
                ),
                if (onSignOut != null) ...[
                  const SizedBox(height: 16),
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
              ],
            ),
          ),
        ),
      ),
    );
  }
}
