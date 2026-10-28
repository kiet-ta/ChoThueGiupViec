import 'dart:async';

import 'package:flutter/material.dart';

import '../../core/models/user_role.dart';
import '../../core/motion/nordic_motion.dart';
import '../../core/theme/nordic_colors.dart';
import '../../core/theme/nordic_typography.dart';
import '../identity/screens/phone_input_screen.dart';
import '../identity/services/token_storage.dart';
import 'customer_shell.dart';
import 'home_actions.dart';
import 'worker_shell.dart';

/// Root screen of TỔ ẤM — Nordic Care (390x844 frame).
/// Signed out: the welcome with a brand hero and one card per role (MOB-UI-08).
/// Signed-in customer: [CustomerShell] with the bottom navigation (MOB-UI-02).
/// Signed-in worker: [WorkerShell] with its bottom navigation (MOB-UI-03).
class HomeScreen extends StatefulWidget {
  const HomeScreen({super.key});

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  /// The signed-in user, or null: the Home shows the actions of this role (MOB-NAV).
  AuthUserView? get _session {
    final storage = TokenStorage();
    final user = storage.currentUser;
    if (!storage.isAuthenticated || user == null) return null;
    return AuthUserView(user.role, user.id);
  }

  /// Back from a pushed screen (the login in particular): show the state of the session as it is now.
  void _refresh() {
    if (!mounted) return;
    setState(() {});
  }

  void _openLogin(AppRole role) {
    Navigator.push(
      context,
      MaterialPageRoute(builder: (context) => PhoneInputScreen(role: role)),
    ).then((_) => _refresh());
  }

  void _signOut() {
    TokenStorage().clear();
    _refresh();
  }

  @override
  Widget build(BuildContext context) {
    final session = _session;
    if (session?.role == SessionRoles.customer) {
      return CustomerShell(onSignOut: _signOut);
    }
    if (session?.role == SessionRoles.worker) {
      return WorkerShell(onSignOut: _signOut, workerId: session!.id);
    }
    return _welcome();
  }

  /// Signed out (MOB-UI-08): brand hero on top; low on the screen, within thumb reach, one
  /// large card per role saying what that role does in the app, then the sign-in method.
  /// Scrolls on a short screen.
  Widget _welcome() {
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      body: SafeArea(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 390.0),
            child: LayoutBuilder(
              builder: (context, constraints) => SingleChildScrollView(
                padding: const EdgeInsets.fromLTRB(20, 16, 20, 24),
                child: ConstrainedBox(
                  constraints: BoxConstraints(
                    minHeight: constraints.maxHeight - 40,
                  ),
                  child: IntrinsicHeight(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        const Expanded(child: _WelcomeHero()),
                        const SizedBox(height: 28),
                        const Text('BẠN LÀ AI?', style: NordicTypography.caps),
                        const SizedBox(height: 12),
                        _RoleCard(
                          key: const Key('welcome-customer'),
                          icon: Icons.cleaning_services_outlined,
                          title: 'Tôi cần giúp việc',
                          description: 'Đặt ca dọn nhà, theo dõi đơn và giữ người giúp việc yêu thích.',
                          filled: true,
                          onTap: () => _openLogin(AppRole.customer),
                        ),
                        const SizedBox(height: 12),
                        _RoleCard(
                          key: const Key('welcome-worker'),
                          icon: Icons.work_outline,
                          title: 'Tôi là người giúp việc',
                          description: 'Nhận việc mới, sắp lịch rảnh và theo dõi thu nhập.',
                          filled: false,
                          onTap: () => _openLogin(AppRole.worker),
                        ),
                        const SizedBox(height: 20),
                        const Row(
                          mainAxisAlignment: MainAxisAlignment.center,
                          children: [
                            Icon(
                              Icons.lock_outline,
                              size: 14,
                              color: NordicColors.textSecondary,
                            ),
                            SizedBox(width: 6),
                            Flexible(
                              child: Text(
                                'Đăng nhập bằng số điện thoại và mã OTP',
                                style: NordicTypography.bodySmall,
                              ),
                            ),
                          ],
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// Photos of the welcome slideshow (Unsplash License, assets/images/welcome/CREDITS.txt) and the
/// work each one shows (spec: cleaning shifts; the worker brings the tool kit, ADR-0010).
const _welcomePhotos = [
  (
    'assets/images/welcome/mop-living-room.jpg',
    Icons.cleaning_services_outlined,
    'Lau sàn',
  ),
  (
    'assets/images/welcome/vacuum-sofa.jpg',
    Icons.weekend_outlined,
    'Hút bụi sofa',
  ),
  (
    'assets/images/welcome/broom-kitchen.jpg',
    Icons.kitchen_outlined,
    'Dọn bếp',
  ),
  (
    'assets/images/welcome/caddy-stairs.jpg',
    Icons.handyman_outlined,
    'Thợ mang đủ dụng cụ',
  ),
];

/// The brand block of the welcome: housework photos fill the whole card as a slideshow, the logo
/// on top and the caption, dots and promise at the bottom sit on them. The card grows to fill
/// the height the role cards leave free.
///
/// The slideshow moves on to the next photo every [_interval] (sliding, or jumping when the
/// platform asks for reduced motion), can be swiped and loops without end. The photos are
/// decorative: hidden from screen readers.
class _WelcomeHero extends StatefulWidget {
  const _WelcomeHero();

  @override
  State<_WelcomeHero> createState() => _WelcomeHeroState();
}

class _WelcomeHeroState extends State<_WelcomeHero> {
  static const _interval = Duration(milliseconds: 3500);
  static final _slideCurve = SpringCurve(NordicMotion.standard);

  final _pages = PageController();
  Timer? _timer;
  int _page = 0;

  @override
  void initState() {
    super.initState();
    _timer = Timer.periodic(_interval, (_) => _next());
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    for (final (asset, _, _) in _welcomePhotos) {
      precacheImage(AssetImage(asset), context);
    }
  }

  @override
  void dispose() {
    _timer?.cancel();
    _pages.dispose();
    super.dispose();
  }

  void _next() {
    if (!mounted || !_pages.hasClients) return;
    final target = (_pages.page?.round() ?? _page) + 1;
    if (NordicMotion.reduced(context)) {
      _pages.jumpToPage(target);
    } else {
      // The standard spring (MOB-UI-12): starts gently, settles without overshoot.
      _pages.animateToPage(
        target,
        duration: NordicMotion.standard.settleDuration,
        curve: _slideCurve,
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final current = _page % _welcomePhotos.length;
    final (_, icon, caption) = _welcomePhotos[current];
    return ClipRRect(
      borderRadius: BorderRadius.circular(28),
      child: Stack(
        children: [
          // Positioned layers do not count in the card's height: the content below sets it.
          Positioned.fill(
            child: ExcludeSemantics(
              // No item count: the pages go on for ever, page n shows photo n % length.
              child: PageView.builder(
                controller: _pages,
                onPageChanged: (page) => setState(() => _page = page),
                itemBuilder: (context, page) => Image.asset(
                  _welcomePhotos[page % _welcomePhotos.length].$1,
                  fit: BoxFit.cover,
                ),
              ),
            ),
          ),
          // Darker top and bottom so the white logo name and text stay readable on any photo.
          Positioned.fill(
            child: IgnorePointer(
              child: DecoratedBox(
                decoration: BoxDecoration(
                  gradient: LinearGradient(
                    begin: Alignment.topCenter,
                    end: Alignment.bottomCenter,
                    stops: const [0, 0.3, 0.5, 1],
                    colors: [
                      NordicColors.contrastDark.withValues(alpha: 0.45),
                      NordicColors.contrastDark.withValues(alpha: 0),
                      NordicColors.contrastDark.withValues(alpha: 0.1),
                      NordicColors.contrastDark.withValues(alpha: 0.82),
                    ],
                  ),
                ),
              ),
            ),
          ),
          // The text lets swipes through to the photos.
          IgnorePointer(
            child: ConstrainedBox(
              constraints: const BoxConstraints(
                minWidth: double.infinity,
                minHeight: 380,
              ),
              child: Padding(
                padding: const EdgeInsets.fromLTRB(24, 24, 24, 28),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    _brand(),
                    const SizedBox(height: 120),
                    const Spacer(),
                    Row(
                      children: [
                        Expanded(
                          child: Align(
                            alignment: Alignment.centerLeft,
                            child: _caption(icon, caption),
                          ),
                        ),
                        const SizedBox(width: 8),
                        for (var i = 0; i < _welcomePhotos.length; i++)
                          AnimatedContainer(
                            duration: const Duration(milliseconds: 250),
                            margin: const EdgeInsets.only(left: 5),
                            width: i == current ? 18 : 6,
                            height: 6,
                            decoration: BoxDecoration(
                              color: NordicColors.textInverse.withValues(
                                alpha: i == current ? 1 : 0.55,
                              ),
                              borderRadius: BorderRadius.circular(9999),
                            ),
                          ),
                      ],
                    ),
                    const SizedBox(height: 18),
                    _promise(),
                  ],
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _brand() {
    return Row(
      children: [
        Container(
          width: 44,
          height: 44,
          decoration: BoxDecoration(
            color: NordicColors.primary,
            borderRadius: BorderRadius.circular(14),
          ),
          child: const Icon(
            Icons.home_outlined,
            color: NordicColors.textInverse,
            size: 24,
          ),
        ),
        const SizedBox(width: 12),
        Text(
          'TỔ ẤM',
          style: NordicTypography.titleMedium.copyWith(
            letterSpacing: 1.2,
            color: NordicColors.textInverse,
          ),
        ),
      ],
    );
  }

  Widget _promise() {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        // Narrow box: the promise breaks after the comma, one half per line.
        ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 210),
          child: Text(
            'Nhà sạch, lòng nhẹ.',
            style: NordicTypography.greeting.copyWith(
              fontSize: 38,
              color: NordicColors.textInverse,
            ),
          ),
        ),
        const SizedBox(height: 8),
        Text(
          'Đặt người giúp việc theo từng ca.',
          style: NordicTypography.bodyRegular.copyWith(
            color: NordicColors.textInverse.withValues(alpha: 0.88),
          ),
        ),
      ],
    );
  }

  Widget _caption(IconData icon, String caption) {
    return Container(
      key: const Key('welcome-photo-caption'),
      padding: const EdgeInsets.fromLTRB(8, 6, 12, 6),
      decoration: BoxDecoration(
        color: NordicColors.surface,
        borderRadius: BorderRadius.circular(9999),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 16, color: NordicColors.primary),
          const SizedBox(width: 6),
          Flexible(
            child: Text(
              caption,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: NordicTypography.labelMedium.copyWith(
                color: NordicColors.primary,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// One role of the welcome: icon, title, what the role does in the app, chevron.
/// [filled] is the primary (green) card; the other is a white card with a border.
class _RoleCard extends StatelessWidget {
  final IconData icon;
  final String title;
  final String description;
  final bool filled;
  final VoidCallback onTap;

  const _RoleCard({
    super.key,
    required this.icon,
    required this.title,
    required this.description,
    required this.filled,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    final foreground = filled
        ? NordicColors.textInverse
        : NordicColors.textTitle;
    final muted = filled
        ? NordicColors.textInverse.withValues(alpha: 0.82)
        : NordicColors.textSecondary;
    final shape = RoundedRectangleBorder(
      borderRadius: BorderRadius.circular(22),
      side: filled
          ? BorderSide.none
          : const BorderSide(color: NordicColors.border),
    );
    return Semantics(
      button: true,
      child: Material(
        color: filled ? NordicColors.primary : NordicColors.surface,
        shape: shape,
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: onTap,
          child: Padding(
            padding: const EdgeInsets.fromLTRB(18, 18, 14, 18),
            child: Row(
              children: [
                Container(
                  width: 48,
                  height: 48,
                  decoration: BoxDecoration(
                    color: filled
                        ? NordicColors.textInverse.withValues(alpha: 0.16)
                        : NordicColors.primaryFixed.withValues(alpha: 0.6),
                    shape: BoxShape.circle,
                  ),
                  child: Icon(
                    icon,
                    size: 24,
                    color: filled
                        ? NordicColors.textInverse
                        : NordicColors.primary,
                  ),
                ),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        title,
                        style: NordicTypography.titleSmall.copyWith(
                          fontSize: 16,
                          color: foreground,
                        ),
                      ),
                      const SizedBox(height: 4),
                      Text(
                        description,
                        style: NordicTypography.bodyRegular.copyWith(
                          fontSize: 13,
                          height: 1.45,
                          color: muted,
                        ),
                      ),
                    ],
                  ),
                ),
                const SizedBox(width: 8),
                Icon(Icons.chevron_right, color: muted),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// The role and id of the signed-in user, as the Home reads them.
class AuthUserView {
  final String role;
  final int id;

  const AuthUserView(this.role, this.id);
}
