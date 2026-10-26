import 'package:flutter/material.dart';

import '../../core/theme/nordic_colors.dart';
import '../dispatch/routes.dart';
import '../payouts/screens/payout_screen.dart';
import '../workers/screens/worker_screen.dart';
import '../workers/screens/worker_slots_screen.dart';
import 'worker_today_tab.dart';

/// The signed-in worker's app (ui-templates `worker/*`, `assets/app.js`): four tabs and a raised centre button.
/// Hôm nay · Lịch · (Việc) · Thu nhập · Tài khoản. Detail screens are pushed on top and have no bottom bar.
class WorkerShell extends StatefulWidget {
  /// Signs out; the Home then shows the welcome again.
  final VoidCallback onSignOut;

  /// The worker's id, shown on the Today tab until a profile endpoint exists.
  final int workerId;

  /// Tab contents, replaceable in tests (index 0..3).
  final List<Widget>? tabs;

  const WorkerShell({
    super.key,
    required this.onSignOut,
    required this.workerId,
    this.tabs,
  });

  @override
  State<WorkerShell> createState() => _WorkerShellState();
}

class _WorkerShellState extends State<WorkerShell> {
  int _tab = 0;

  /// A tab is built the first time the worker opens it: the slots screen shares the position and must not do so at login.
  final _visited = {0};

  /// A counter per tab: pushing a new key rebuilds (reloads) the tab when the worker comes back to it.
  final _generation = [0, 0, 0, 0];

  void _select(int index) => setState(() {
    if (index == _tab) return;
    _tab = index;
    _visited.add(index);
    _generation[index]++;
  });

  void _openOffers() {
    Navigator.of(context).pushNamed(DispatchRoutes.offers).then((_) {
      if (mounted) setState(() => _generation[_tab]++);
    });
  }

  Widget _build(int index) {
    final custom = widget.tabs;
    if (custom != null) return custom[index];
    final key = ValueKey('worker-$index-${_generation[index]}');
    return switch (index) {
      0 => WorkerTodayTab(
        key: key,
        workerId: widget.workerId,
        onShowSlots: () => _select(1),
        onShowIncome: () => _select(2),
        onOpenOffers: _openOffers,
      ),
      1 => WorkerSlotsScreen(key: key),
      2 => PayoutScreen(key: key),
      _ => WorkerScreen(
        key: key,
        workerId: widget.workerId,
        onSignOut: widget.onSignOut,
      ),
    };
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: IndexedStack(
        index: _tab,
        children: [
          for (var i = 0; i < 4; i++)
            _visited.contains(i) ? _build(i) : const SizedBox.shrink(),
        ],
      ),
      floatingActionButton: SizedBox(
        width: 58,
        height: 58,
        child: FloatingActionButton(
          key: const Key('worker-shell-job'),
          tooltip: 'Việc',
          onPressed: _openOffers,
          child: const Icon(Icons.work_outline, size: 26),
        ),
      ),
      floatingActionButtonLocation: FloatingActionButtonLocation.centerDocked,
      bottomNavigationBar: BottomAppBar(
        color: NordicColors.surface,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        height: 68,
        padding: EdgeInsets.zero,
        shape: const CircularNotchedRectangle(),
        notchMargin: 6,
        child: DecoratedBox(
          decoration: const BoxDecoration(
            border: Border(top: BorderSide(color: NordicColors.border)),
          ),
          child: Row(
            children: [
              _item(0, Icons.today_outlined, Icons.today, 'Hôm nay'),
              _item(
                1,
                Icons.calendar_month_outlined,
                Icons.calendar_month,
                'Lịch',
              ),
              const Expanded(
                child: Align(
                  alignment: Alignment.bottomCenter,
                  child: Padding(
                    padding: EdgeInsets.only(bottom: 10),
                    child: Text(
                      'Việc',
                      style: TextStyle(
                        fontSize: 11,
                        fontWeight: FontWeight.w600,
                        color: NordicColors.primary,
                      ),
                    ),
                  ),
                ),
              ),
              _item(2, Icons.payments_outlined, Icons.payments, 'Thu nhập'),
              _item(3, Icons.person_outline, Icons.person, 'Tài khoản'),
            ],
          ),
        ),
      ),
    );
  }

  Widget _item(int index, IconData icon, IconData selectedIcon, String label) {
    final selected = _tab == index;
    final color = selected ? NordicColors.primary : NordicColors.textSecondary;
    return Expanded(
      child: InkResponse(
        key: Key('worker-tab-$index'),
        onTap: () => _select(index),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(selected ? selectedIcon : icon, color: color, size: 24),
            const SizedBox(height: 3),
            Text(
              label,
              style: TextStyle(
                fontSize: 11,
                fontWeight: selected ? FontWeight.w700 : FontWeight.w500,
                color: color,
              ),
            ),
          ],
        ),
      ),
    );
  }
}
