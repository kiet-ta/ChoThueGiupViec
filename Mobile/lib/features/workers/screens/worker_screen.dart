import 'package:flutter/material.dart';

import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/avatar_initials.dart';
import '../../../core/widgets/grouped_list_card.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/nordic_text_input.dart';
import '../../agencies/routes.dart';
import '../../customers/widgets/sign_out_dialog.dart';
import '../../disputes/models/dispute_models.dart';
import '../../disputes/routes.dart';
import '../../disputes/screens/dispute_screen.dart';
import '../models/worker_work_area_models.dart';
import '../routes.dart';
import '../services/workers_profile_service.dart';
import '../services/workers_work_area_service.dart';

/// The worker's Tài khoản tab and the `/workers/profile` screen: who is signed in
/// and the links to eKYC, the work area, the company schedule and disputes. The rating and criteria averages of the template need an API
/// that does not exist yet, so they are not shown.
/// MOB-UI-24 (`Mobile/DESIGN-SYSTEM-WORKER.md`): `NordicPage` (tab, or pushed when it has a back route), tokens only.
/// MOB-UI-26: an agency worker is verified by the agency, so the eKYC row is not shown to them; the company roster row is
/// shown to agency workers only (a freelancer sets the shifts in the Lịch tab).
/// MOB-WORKER-AREA-01 (#556): a freelancer also has the row "Nơi làm việc" (where they work, decisions D24).
class WorkerScreen extends StatefulWidget {
  /// The worker's id from the login; there is no profile endpoint to read a name from.
  final int? workerId;

  /// Signs out; without it (the pushed screen) the sign-out row is not shown.
  final VoidCallback? onSignOut;

  /// Reads the profile to tell an agency worker from a freelancer; replaceable in tests.
  final WorkersProfileService? profile;

  /// Reads the registered work area for the subtitle of its row; replaceable in tests.
  final WorkersWorkAreaService? workArea;

  const WorkerScreen({
    super.key,
    this.workerId,
    this.onSignOut,
    this.profile,
    this.workArea,
  });

  @override
  State<WorkerScreen> createState() => _WorkerScreenState();
}

class _WorkerScreenState extends State<WorkerScreen> {
  /// Until the profile says the worker belongs to an agency (or if it cannot be read) the eKYC row stays, and the company
  /// roster row is hidden: a freelancer must never lose the way to eKYC because of a network error.
  bool _agencyWorker = false;

  /// The worker's name from the profile; until it loads (or if it has none) the placeholder below is shown.
  String? _fullName;

  /// The registered work area, or null while it loads or when it cannot be read.
  WorkerWorkArea? _area;

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
    _loadArea();
  }

  void _loadArea() {
    (widget.workArea ?? WorkersWorkAreaService())
        .getWorkArea()
        .then((response) {
          if (mounted) setState(() => _area = response.data);
        })
        .catchError((_) {});
  }

  /// Asks the worker for a new name, saves it and shows it; a refused name keeps the old one.
  Future<void> _editName() async {
    final service = widget.profile ?? WorkersProfileService();
    final saved = await showDialog<String>(
      context: context,
      builder: (_) => _EditNameDialog(
        initialName: _fullName ?? '',
        save: service.updateFullName,
      ),
    );
    if (saved != null && mounted) setState(() => _fullName = saved);
  }

  /// Asks first, like the customer screens, then signs out.
  Future<void> _confirmSignOut(VoidCallback onSignOut) async {
    if (await confirmCustomerSignOut(context) && mounted) onSignOut();
  }

  void _open(BuildContext context, String route, {Object? arguments}) {
    Navigator.of(context).pushNamed(route, arguments: arguments);
  }

  String get _areaSubtitle {
    final area = _area;
    if (area == null || !area.isSet) return 'Chưa đăng ký';
    final districts = area.districts.isEmpty
        ? ''
        : ' · ${area.districts.length} phường/xã';
    return '${area.radiusKm} km$districts';
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
            IconButton(
              key: const Key('worker-account-edit-name'),
              tooltip: 'Sửa tên',
              icon: const Icon(Icons.edit_outlined),
              onPressed: _editName,
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
          if (!_agencyWorker)
            GroupedListItem(
              key: const Key('worker-account-work-area'),
              icon: Icons.place_outlined,
              title: 'Nơi làm việc',
              subtitle: _areaSubtitle,
              onTap: () =>
                  Navigator.of(context)
                      .pushNamed(WorkerRoutes.workArea)
                      .then((_) => _loadArea()),
            ),
          // The company roster only exists for an agency worker; a freelancer sets the shifts in the Lịch tab.
          if (_agencyWorker)
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
              onTap: () => _confirmSignOut(onSignOut),
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

/// The dialog that edits the worker's name. It pops the name the server kept, or null when cancelled.
class _EditNameDialog extends StatefulWidget {
  final String initialName;
  final Future<String> Function(String name) save;

  const _EditNameDialog({required this.initialName, required this.save});

  @override
  State<_EditNameDialog> createState() => _EditNameDialogState();
}

class _EditNameDialogState extends State<_EditNameDialog> {
  late final TextEditingController _controller = TextEditingController(
    text: widget.initialName,
  );
  bool _saving = false;
  String? _error;

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _fail(String message) {
    if (!mounted) return;
    setState(() {
      _saving = false;
      _error = message;
    });
  }

  Future<void> _submit() async {
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      final name = await widget.save(_controller.text);
      if (mounted) Navigator.pop(context, name);
    } on ArgumentError catch (e) {
      _fail(e.message.toString());
    } on ApiException catch (e) {
      _fail('Chưa lưu được tên: ${e.message}');
    } catch (_) {
      _fail('Chưa lưu được tên. Bạn thử lại sau nhé.');
    }
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: const Text('Sửa tên', style: NordicTypography.h3),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          NordicTextInput(
            label: 'Họ và tên',
            controller: _controller,
            textInputAction: TextInputAction.done,
          ),
          if (_error != null) ...[
            const SizedBox(height: NordicSpace.xs),
            Text(
              _error!,
              key: const Key('worker-name-error'),
              style: NordicTypography.bodySmall.copyWith(
                color: NordicColors.error,
              ),
            ),
          ],
        ],
      ),
      actions: [
        TextButton(
          key: const Key('worker-name-cancel'),
          onPressed: _saving ? null : () => Navigator.pop(context),
          child: const Text(
            'Huỷ',
            style: TextStyle(color: NordicColors.textSecondary),
          ),
        ),
        ElevatedButton(
          key: const Key('worker-name-save'),
          onPressed: _saving ? null : _submit,
          child: const Text('Lưu'),
        ),
      ],
    );
  }
}
