import 'dart:ui' show ImageFilter;

import 'package:flutter/material.dart';

import '../../core/motion/nordic_motion.dart';
import '../../core/theme/nordic_colors.dart';
import '../../core/theme/nordic_elevation.dart';
import '../../core/theme/nordic_radius.dart';
import '../../core/theme/nordic_spacing.dart';
import '../../core/widgets/pressable.dart';
import '../booking/routes.dart';
import '../booking/screens/order_history_screen.dart';
import '../customers/screens/favorite_workers_screen.dart';
import 'customer_account_tab.dart';
import 'customer_home_tab.dart';

/// The signed-in customer's app (ui-templates `customer/*`, `assets/app.js`): four tabs and a raised
/// centre button. Trang chủ · Đơn · (+ Đặt ca) · Yêu thích · Tài khoản. Detail screens are pushed on top
/// and have no bottom bar. The bar is a translucent material with a soft pill on the selected tab
/// (MOB-UI-14, `Mobile/DESIGN-SYSTEM.md`).
class CustomerShell extends StatefulWidget {
  /// Signs out; the Home then shows the welcome again.
  final VoidCallback onSignOut;

  /// Tab contents, replaceable in tests (index 0..3).
  final List<Widget>? tabs;

  const CustomerShell({super.key, required this.onSignOut, this.tabs});

  @override
  State<CustomerShell> createState() => _CustomerShellState();
}

class _CustomerShellState extends State<CustomerShell> {
  int _tab = 0;

  /// A counter per tab: pushing a new key rebuilds (reloads) the tab when the customer comes back to it.
  final _generation = [0, 0, 0, 0];

  void _select(int index) => setState(() {
    if (index == _tab) return;
    _tab = index;
    _generation[index]++;
  });

  void _book() {
    Navigator.of(context).pushNamed(BookingRoutes.booking).then((_) {
      if (mounted) setState(() => _generation[_tab]++);
    });
  }

  List<Widget> get _tabs =>
      widget.tabs ??
      [
        CustomerHomeTab(
          key: ValueKey('home-${_generation[0]}'),
          onShowFavorites: () => _select(2),
        ),
        OrderHistoryScreen(key: ValueKey('orders-${_generation[1]}')),
        FavoriteWorkersScreen(key: ValueKey('favorites-${_generation[2]}')),
        CustomerAccountTab(
          key: ValueKey('account-${_generation[3]}'),
          onSignOut: widget.onSignOut,
          onShowOrders: () => _select(1),
          onShowFavorites: () => _select(2),
        ),
      ];

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      // The bar is a translucent material: the tab content scrolls under it (DESIGN-SYSTEM §4).
      extendBody: true,
      body: IndexedStack(index: _tab, children: _tabs),
      // The deep-green centre button floats on the soft green-tinted CTA shadow (DESIGN-SYSTEM §3.5).
      floatingActionButton: Pressable(
        child: DecoratedBox(
          decoration: const BoxDecoration(
            shape: BoxShape.circle,
            boxShadow: NordicElevation.cta,
          ),
          child: SizedBox(
            width: 60,
            height: 60,
            child: FloatingActionButton(
              key: const Key('customer-shell-book'),
              tooltip: 'Đặt ca',
              elevation: 0,
              focusElevation: 0,
              hoverElevation: 0,
              highlightElevation: 0,
              onPressed: _book,
              child: const Icon(Icons.add, size: 28),
            ),
          ),
        ),
      ),
      floatingActionButtonLocation: FloatingActionButtonLocation.centerDocked,
      bottomNavigationBar: _bar(context),
    );
  }

  Widget _bar(BuildContext context) {
    final solid = MediaQuery.maybeHighContrastOf(context) ?? false;
    final bar = DecoratedBox(
      decoration: BoxDecoration(
        color: solid
            ? NordicColors.surface
            : NordicColors.surface.withValues(alpha: 0.86),
        border: const Border(top: BorderSide(color: NordicColors.border)),
      ),
      child: SafeArea(
        top: false,
        child: SizedBox(
          height: 64,
          child: Row(
            children: [
              _item(0, Icons.home_outlined, Icons.home, 'Trang chủ'),
              _item(1, Icons.receipt_long_outlined, Icons.receipt_long, 'Đơn'),
              const Expanded(
                child: Align(
                  alignment: Alignment.bottomCenter,
                  child: Padding(
                    padding: EdgeInsets.only(bottom: NordicSpace.xs),
                    child: Text(
                      'Đặt ca',
                      style: TextStyle(
                        fontSize: 11,
                        fontWeight: FontWeight.w600,
                        color: NordicColors.primary,
                      ),
                    ),
                  ),
                ),
              ),
              _item(2, Icons.favorite_outline, Icons.favorite, 'Yêu thích'),
              _item(3, Icons.person_outline, Icons.person, 'Tài khoản'),
            ],
          ),
        ),
      ),
    );
    if (solid) return bar;
    return ClipRect(
      child: BackdropFilter(
        filter: ImageFilter.blur(sigmaX: 20, sigmaY: 20),
        child: bar,
      ),
    );
  }

  Widget _item(int index, IconData icon, IconData selectedIcon, String label) {
    final selected = _tab == index;
    final color = selected ? NordicColors.primary : NordicColors.textSecondary;
    final reduced = NordicMotion.reduced(context);
    return Expanded(
      child: InkResponse(
        key: Key('customer-tab-$index'),
        onTap: () => _select(index),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            // The selected tab sits on a soft green pill that grows in on the standard spring.
            AnimatedContainer(
              duration: reduced
                  ? Duration.zero
                  : NordicMotion.standard.settleDuration,
              curve: _pillCurve,
              padding: EdgeInsets.symmetric(
                horizontal: selected ? NordicSpace.md : NordicSpace.xs,
                vertical: 2,
              ),
              decoration: BoxDecoration(
                color: selected
                    ? NordicColors.primaryFixed
                    : NordicColors.primaryFixed.withValues(alpha: 0),
                borderRadius: NordicRadius.pill,
              ),
              child: Icon(
                selected ? selectedIcon : icon,
                color: color,
                size: 24,
              ),
            ),
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

  static final _pillCurve = SpringCurve(NordicMotion.standard);
}
