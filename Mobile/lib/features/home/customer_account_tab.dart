import 'package:flutter/material.dart';

import '../../core/theme/nordic_colors.dart';
import '../../core/theme/nordic_spacing.dart';
import '../../core/theme/nordic_typography.dart';
import '../../core/widgets/avatar_initials.dart';
import '../../core/widgets/dark_header.dart';
import '../../core/widgets/glass_panel.dart';
import '../../core/widgets/grouped_list_card.dart';
import '../../core/widgets/raised_panel.dart';
import '../../core/widgets/section_title.dart';
import '../booking/services/order_service.dart';
import '../customers/models/customer_profile.dart';
import '../customers/routes.dart';
import '../customers/services/customer_address_service.dart';
import '../customers/services/customer_profile_service.dart';
import '../customers/widgets/sign_out_dialog.dart';
import '../disputes/models/dispute_models.dart';
import '../disputes/routes.dart';
import '../disputes/screens/dispute_screen.dart';

/// The customer's Tài khoản tab (ui-templates `customer/account.html`): who is signed in, three counts
/// read from real answers (orders = `total` of the order list, addresses, date joined) and the links
/// that used to be the Home's action list.
/// MOB-UI-14: `NordicPage.tab` with the large title, pressable profile card (`Mobile/DESIGN-SYSTEM.md`).
class CustomerAccountTab extends StatefulWidget {
  final VoidCallback onSignOut;
  final VoidCallback? onShowOrders;

  /// Switches to the Yêu thích tab; the row is hidden when it is null.
  final VoidCallback? onShowFavorites;
  final CustomerProfileService? profile;
  final CustomerAddressService? addresses;
  final IOrderService? orders;

  const CustomerAccountTab({
    super.key,
    required this.onSignOut,
    this.onShowOrders,
    this.onShowFavorites,
    this.profile,
    this.addresses,
    this.orders,
  });

  @override
  State<CustomerAccountTab> createState() => _CustomerAccountTabState();
}

class _CustomerAccountTabState extends State<CustomerAccountTab> {
  late final CustomerProfileService _profileService =
      widget.profile ?? CustomerProfileService();
  late final CustomerAddressService _addressService =
      widget.addresses ?? CustomerAddressService();
  late final IOrderService _orderService = widget.orders ?? OrderService();

  CustomerProfile? _profile;
  int? _orderCount;
  int? _addressCount;

  @override
  void initState() {
    super.initState();
    _load();
  }

  void _load() {
    _profileService
        .getProfile()
        .then((p) {
          if (mounted) setState(() => _profile = p);
        })
        .catchError((_) {});
    _orderService
        .listOrders(pageSize: 1)
        .then((page) {
          if (mounted) setState(() => _orderCount = page.total);
        })
        .catchError((_) {});
    _addressService
        .getAddresses()
        .then((list) {
          if (mounted) setState(() => _addressCount = list.length);
        })
        .catchError((_) {});
  }

  void _open(String route, {Object? arguments}) {
    Navigator.of(context).pushNamed(route, arguments: arguments).then((result) {
      if (!mounted) return;
      // Signed out on the profile screen: finish it the same way as the row below.
      if (result == customerSignedOutResult) {
        widget.onSignOut();
      } else {
        _load();
      }
    });
  }

  /// Asks first, like the profile screen, then signs out through the shell.
  Future<void> _signOut() async {
    if (await confirmCustomerSignOut(context) && mounted) widget.onSignOut();
  }

  /// `0912 345 678`.
  static String _phone(String raw) => raw.length == 10
      ? '${raw.substring(0, 4)} ${raw.substring(4, 7)} ${raw.substring(7)}'
      : raw;

  /// How far the raised panel rises over the dark header.
  static const _overlap = 56.0;

  @override
  Widget build(BuildContext context) {
    final profile = _profile;
    final bottom = MediaQuery.paddingOf(context).bottom;
    final name = (profile == null || profile.fullName.trim().isEmpty)
        ? 'Khách hàng'
        : profile.fullName;
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      body: ListView(
        // The shell's bar and its raised centre button sit over the end of this tab.
        padding: EdgeInsets.only(bottom: 120 + bottom - _overlap),
        children: [
          DarkHeader(
            overlap: _overlap,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'Tài khoản',
                  style: NordicTypography.display.copyWith(
                    color: NordicColors.textInverse,
                  ),
                ),
                const SizedBox(height: NordicSpace.md),
                Row(
                  children: [
                    AvatarInitials(
                      profile?.fullName ?? '',
                      size: 60,
                      background: NordicColors.primaryFixed,
                      foreground: NordicColors.primary,
                    ),
                    const SizedBox(width: NordicSpace.sm),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            name,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: NordicTypography.titleMedium.copyWith(
                              color: NordicColors.textInverse,
                            ),
                          ),
                          if (profile != null)
                            Text(
                              _phone(profile.phoneNumber),
                              style: NordicTypography.bodyRegular.copyWith(
                                color: NordicColors.onDarkMuted,
                              ),
                            ),
                        ],
                      ),
                    ),
                    const SizedBox(width: NordicSpace.xs),
                    Semantics(
                      button: true,
                      label: 'Sửa hồ sơ',
                      child: GlassPanel(
                        key: const Key('account-profile'),
                        pill: true,
                        onTap: () => _open(CustomerRoutes.customerProfile),
                        padding: const EdgeInsets.symmetric(
                          horizontal: NordicSpace.md,
                          vertical: NordicSpace.xs,
                        ),
                        child: ExcludeSemantics(
                          child: Text(
                            'Sửa hồ sơ',
                            style: NordicTypography.labelMedium.copyWith(
                              color: NordicColors.textInverse,
                            ),
                          ),
                        ),
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),
          Transform.translate(
            offset: const Offset(0, -_overlap),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                RaisedPanel(
                  padding: const EdgeInsets.symmetric(
                    vertical: NordicSpace.md,
                    horizontal: NordicSpace.xs,
                  ),
                  child: IntrinsicHeight(
                    child: Row(
                      children: [
                        Expanded(
                          child: _count('${_orderCount ?? '—'}', 'Đơn đã đặt'),
                        ),
                        const VerticalDivider(
                          width: 1,
                          color: NordicColors.surfaceContainer,
                        ),
                        Expanded(
                          child: _count('${_addressCount ?? '—'}', 'Địa chỉ'),
                        ),
                        const VerticalDivider(
                          width: 1,
                          color: NordicColors.surfaceContainer,
                        ),
                        Expanded(
                          child: _count(
                            profile?.createdAt == null
                                ? '—'
                                : profile!.formattedCreatedAt,
                            'Ngày tham gia',
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
                Padding(
                  padding: const EdgeInsets.symmetric(
                    horizontal: NordicSpace.gutter,
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      const SectionTitle('Đặt ca'),
                      GroupedListCard(
                        items: [
                          GroupedListItem(
                            key: const Key('account-addresses'),
                            icon: Icons.location_on_outlined,
                            title: 'Địa chỉ của tôi',
                            onTap: () => _open(CustomerRoutes.addressList),
                          ),
                          GroupedListItem(
                            key: const Key('account-orders'),
                            icon: Icons.receipt_long_outlined,
                            title: 'Đơn đã đặt',
                            subtitle: 'Theo dõi, thanh toán, đánh giá',
                            onTap: widget.onShowOrders,
                          ),
                          if (widget.onShowFavorites != null)
                            GroupedListItem(
                              key: const Key('account-favorites'),
                              icon: Icons.favorite_border,
                              title: 'Người giúp việc yêu thích',
                              onTap: widget.onShowFavorites,
                            ),
                        ],
                      ),
                      const SectionTitle('Hỗ trợ'),
                      GroupedListCard(
                        items: [
                          GroupedListItem(
                            key: const Key('account-disputes'),
                            icon: Icons.flag_outlined,
                            title: 'Khiếu nại',
                            subtitle: 'Theo dõi khiếu nại đã gửi',
                            onTap: () => _open(
                              DisputeRoutes.disputes,
                              arguments: const DisputeScreenArgs(
                                role: DisputeRole.customer,
                              ),
                            ),
                          ),
                        ],
                      ),
                      const SizedBox(height: NordicSpace.md),
                      GroupedListCard(
                        items: [
                          GroupedListItem(
                            key: const Key('home-sign-out'),
                            icon: Icons.logout,
                            title: 'Đăng xuất',
                            danger: true,
                            onTap: _signOut,
                          ),
                        ],
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _count(String value, String label) => Column(
    children: [
      // A full date (dd/mm/yyyy) is wider than a count: it shrinks to fit.
      FittedBox(
        fit: BoxFit.scaleDown,
        child: Text(value, maxLines: 1, style: NordicTypography.numberMedium),
      ),
      const SizedBox(height: NordicSpace.xs),
      Text(
        label,
        textAlign: TextAlign.center,
        style: NordicTypography.bodySmall,
      ),
    ],
  );
}
