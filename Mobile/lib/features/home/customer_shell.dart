import 'dart:async';
import 'dart:ui' show ImageFilter;

import 'package:flutter/material.dart';

import '../../core/motion/nordic_motion.dart';
import '../../core/realtime/realtime_notice.dart';
import '../../core/realtime/realtime_service.dart';
import '../../core/theme/nordic_colors.dart';
import '../../core/theme/nordic_elevation.dart';
import '../../core/theme/nordic_radius.dart';
import '../../core/theme/nordic_spacing.dart';
import '../../core/widgets/pressable.dart';
import '../booking/models/booking_models.dart';
import '../booking/routes.dart';
import '../booking/screens/order_history_screen.dart';
import '../customers/logic/onboarding_logic.dart';
import '../customers/models/customer_profile.dart';
import '../customers/screens/customer_onboarding_screen.dart';
import '../customers/screens/favorite_workers_screen.dart';
import '../customers/services/customer_profile_service.dart';
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
  final int initialTab;

  /// Reads the customer's profile for the onboarding check (MOB-ONBOARD-02). The real app asks the server; a test passes its own.
  /// With [tabs] replaced and no loader the check is skipped, so the existing shell tests stay offline.
  final Future<CustomerProfile> Function()? profileLoader;

  /// The realtime channel (PREM-MOB-01, decisions D37 / D38): the apology of a cancelled Premium order arrives here. The real
  /// app uses the SignalR hub with the session's token; with [tabs] replaced and no channel given the shell stays offline, so
  /// the existing shell tests do not open a socket.
  final IRealtimeService? realtime;

  const CustomerShell({
    super.key,
    required this.onSignOut,
    this.tabs,
    this.initialTab = 0,
    this.profileLoader,
    this.realtime,
  }) : assert(initialTab >= 0 && initialTab < 4);

  @override
  State<CustomerShell> createState() => _CustomerShellState();
}

class _CustomerShellState extends State<CustomerShell> {
  int _tab = 0;

  IRealtimeService? _realtime;
  late final bool _ownsRealtime;
  StreamSubscription<RealtimeNotification>? _notices;
  final _notice = RealtimeNoticePresenter();

  @override
  void initState() {
    super.initState();
    _tab = widget.initialTab;
    _ownsRealtime = widget.realtime == null;
    if (widget.realtime != null || widget.tabs == null) {
      final realtime = _realtime = widget.realtime ?? RealtimeService();
      _notices = realtime.notifications.listen(_onNotice);
      // Best effort: without the hub the app works as before (the order detail still says "Đã huỷ").
      realtime.connect();
    }
    if (widget.profileLoader != null || widget.tabs == null) {
      WidgetsBinding.instance.addPostFrameCallback((_) => _checkOnboarding());
    }
  }

  @override
  void dispose() {
    _notices?.cancel();
    _notice.dismiss();
    final realtime = _realtime;
    if (realtime != null) {
      _ownsRealtime ? realtime.dispose() : realtime.disconnect();
    }
    super.dispose();
  }

  /// D38: a Premium order was cancelled because the partner did not bring enough workers on time. The apology stays on top of
  /// whatever screen is open until it is closed, with a way to the cancelled order; the lists reload so they say "Đã huỷ".
  void _onNotice(RealtimeNotification notice) {
    if (!mounted) return;
    if (notice.topic != RealtimeTopics.premiumOrderCancelled) return;
    setState(() {
      for (var i = 0; i < _generation.length; i++) {
        _generation[i]++;
      }
    });
    final orderId = int.tryParse(notice.data['orderId'] ?? '');
    _notice.show(
      context,
      title: notice.title.isNotEmpty ? notice.title : 'TỔ ẤM xin lỗi bạn',
      body: notice.body,
      actionLabel: 'Xem đơn',
      onAction: orderId == null ? null : () => _openOrder(orderId),
    );
  }

  void _openOrder(int orderId) {
    if (!mounted) return;
    Navigator.of(context)
        .pushNamed(
          BookingRoutes.bookingDetail,
          arguments: OrderDetailArgs(orderId: orderId),
        )
        .then((_) {
          if (mounted) setState(() => _generation[_tab]++);
        });
  }

  /// A customer without a name (a new one, or one who quit the onboarding before giving it) is taken to the onboarding, at every
  /// login until a name exists. A failed call is ignored: it must never block the app.
  Future<void> _checkOnboarding() async {
    try {
      final load = widget.profileLoader ?? CustomerProfileService().getProfile;
      final profile = await load();
      if (!mounted || !needsOnboarding(profile)) return;
      await Navigator.of(context).push<OnboardingResult>(
        MaterialPageRoute(
          fullscreenDialog: true,
          builder: (_) => const CustomerOnboardingScreen(),
        ),
      );
      if (!mounted) return;
      setState(() {
        for (var i = 0; i < _generation.length; i++) {
          _generation[i]++;
        }
      });
    } catch (_) {
      // The app works without the check.
    }
  }

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
      // A replaced tab is reloaded the same way as a real one (a new key), like `worker_shell.dart`.
      widget.tabs?.indexed
          .map(
            (e) => KeyedSubtree(
              key: ValueKey('tab-${e.$1}-${_generation[e.$1]}'),
              child: e.$2,
            ),
          )
          .toList() ??
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
            // One line that shrinks instead of wrapping when the text is scaled up.
            FittedBox(
              fit: BoxFit.scaleDown,
              child: Text(
                label,
                maxLines: 1,
                style: TextStyle(
                  fontSize: 11,
                  fontWeight: selected ? FontWeight.w700 : FontWeight.w500,
                  color: color,
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  static final _pillCurve = SpringCurve(NordicMotion.standard);
}
