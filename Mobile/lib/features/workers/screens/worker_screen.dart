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
import '../services/workers_profile_service.dart';

/// The worker's Tài khoản tab and the `/workers/profile` screen: who is signed in
/// and the links to eKYC, the company schedule and disputes. The rating and criteria averages of the template need an API
/// that does not exist yet, so they are not shown.
/// MOB-UI-24 (`Mobile/DESIGN-SYSTEM-WORKER.md`): `NordicPage` (tab, or pushed when it has a back route), tokens only.
/// An agency worker is verified by the agency, so the eKYC row is not shown to them (MOB-UI-26).
class WorkerScreen extends StatefulWidget {
  /// The worker's id from the login; there is no profile endpoint to read a name from.
  final int? workerId;

  /// Signs out; without it (the pushed screen) the sign-out row is not shown.
  final VoidCallback? onSignOut;

  /// Reads the profile to tell an agency worker from a freelancer; replaceable in tests.
  final WorkersProfileService? profile;

  const WorkerScreen({super.key, this.workerId, this.onSignOut, this.profile});

  @override
  State<WorkerScreen> createState() => _WorkerScreenState();
}

class _WorkerScreenState extends State<WorkerScreen> {
  /// Until the profile says the worker belongs to an agency (or if it cannot be read) the eKYC row stays:
  /// a freelancer must never lose the way to eKYC because of a network error.
  bool _agencyWorker = false;

  /// The worker's name from the profile; until it loads (or if it has none) the placeholder below is shown.
  String? _fullName;

  @override
  void initState() {
    super.initState();
    (widget.profile ?? WorkersProfileService())
        .getSummary()
        .then((summary) {
          if (!mounted) return;
          setState(() {
            _agencyWorker = summary.agencyWorker;
            _fullName = summary.fullName;
          });
        })
        .catchError((_) {});
  }

  void _open(BuildContext context, String route, {Object? arguments}) {
    Navigator.of(context).pushNamed(route, arguments: arguments);
  }

  @override
  Widget build(BuildContext context) {
    final pushed = ModalRoute.of(context)?.canPop ?? false;
    final workerId = widget.workerId;
    final onSignOut = widget.onSignOut;
    final title =
        _fullName ?? (workerId == null ? 'Thợ giúp việc' : 'Thợ #$workerId');
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
          if (!_agencyWorker)
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
