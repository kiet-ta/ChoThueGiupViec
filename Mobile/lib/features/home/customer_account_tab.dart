import 'package:flutter/material.dart';

import '../../core/theme/nordic_colors.dart';
import '../../core/theme/nordic_typography.dart';
import '../../core/widgets/avatar_initials.dart';
import '../../core/widgets/grouped_list_card.dart';
import '../booking/services/order_service.dart';
import '../customers/models/customer_profile.dart';
import '../customers/routes.dart';
import '../customers/services/customer_address_service.dart';
import '../customers/services/customer_profile_service.dart';
import '../disputes/models/dispute_models.dart';
import '../disputes/routes.dart';
import '../disputes/screens/dispute_screen.dart';

/// The customer's Tài khoản tab (ui-templates `customer/account.html`): who is signed in, three counts
/// read from real answers (orders = `total` of the order list, addresses, year joined) and the links
/// that used to be the Home's action list.
class CustomerAccountTab extends StatefulWidget {
  final VoidCallback onSignOut;
  final VoidCallback? onShowOrders;
  final CustomerProfileService? profile;
  final CustomerAddressService? addresses;
  final IOrderService? orders;

  const CustomerAccountTab({
    super.key,
    required this.onSignOut,
    this.onShowOrders,
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
    Navigator.of(context).pushNamed(route, arguments: arguments).then((_) {
      if (mounted) _load();
    });
  }

  /// `0912 345 678`.
  static String _phone(String raw) => raw.length == 10
      ? '${raw.substring(0, 4)} ${raw.substring(4, 7)} ${raw.substring(7)}'
      : raw;

  @override
  Widget build(BuildContext context) {
    final profile = _profile;
    return Scaffold(
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.fromLTRB(20, 8, 20, 32),
          children: [
            const Padding(
              padding: EdgeInsets.only(top: 8, bottom: 16),
              child: Text('Tài khoản', style: NordicTypography.screenTitle),
            ),
            Material(
              color: NordicColors.surface,
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.zero,
                side: const BorderSide(color: NordicColors.border),
              ),
              child: InkWell(
                key: const Key('account-profile'),
                borderRadius: BorderRadius.zero,
                onTap: () => _open(CustomerRoutes.customerProfile),
                child: Padding(
                  padding: const EdgeInsets.all(16),
                  child: Row(
                    children: [
                      AvatarInitials(profile?.fullName ?? '', size: 72),
                      const SizedBox(width: 12),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              (profile == null ||
                                      profile.fullName.trim().isEmpty)
                                  ? 'Khách hàng'
                                  : profile.fullName,
                              style: NordicTypography.titleMedium,
                            ),
                            if (profile != null) ...[
                              const SizedBox(height: 4),
                              Text(
                                _phone(profile.phoneNumber),
                                style: NordicTypography.bodySmall,
                              ),
                            ],
                          ],
                        ),
                      ),
                      const Icon(
                        Icons.chevron_right,
                        color: NordicColors.borderVariant,
                      ),
                    ],
                  ),
                ),
              ),
            ),
            const SizedBox(height: 16),
            Row(
              children: [
                Expanded(child: _count('${_orderCount ?? '–'}', 'Đơn đã đặt')),
                const SizedBox(width: 8),
                Expanded(child: _count('${_addressCount ?? '–'}', 'Địa chỉ')),
                const SizedBox(width: 8),
                Expanded(
                  child: _count(
                    profile?.createdAt == null
                        ? '–'
                        : '${profile!.createdAt!.year}',
                    'Tham gia',
                  ),
                ),
              ],
            ),
            const SizedBox(height: 24),
            GroupedListCard(
              items: [
                GroupedListItem(
                  key: const Key('account-addresses'),
                  icon: Icons.location_on_outlined,
                  title: 'Sổ địa chỉ',
                  onTap: () => _open(CustomerRoutes.addressList),
                ),
                GroupedListItem(
                  key: const Key('account-orders'),
                  icon: Icons.receipt_long_outlined,
                  title: 'Lịch sử đơn',
                  onTap: widget.onShowOrders,
                ),
                GroupedListItem(
                  key: const Key('account-disputes'),
                  icon: Icons.flag_outlined,
                  title: 'Khiếu nại',
                  onTap: () => _open(
                    DisputeRoutes.disputes,
                    arguments: const DisputeScreenArgs(
                      role: DisputeRole.customer,
                    ),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 16),
            GroupedListCard(
              items: [
                GroupedListItem(
                  key: const Key('home-sign-out'),
                  icon: Icons.logout,
                  title: 'Đăng xuất',
                  danger: true,
                  onTap: widget.onSignOut,
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  Widget _count(String value, String label) => Container(
    padding: const EdgeInsets.symmetric(vertical: 14, horizontal: 8),
    decoration: BoxDecoration(
      color: NordicColors.surface,
      borderRadius: BorderRadius.zero,
      border: Border.all(color: NordicColors.border),
    ),
    child: Column(
      children: [
        Text(value, style: NordicTypography.numberMedium),
        const SizedBox(height: 8),
        Text(
          label,
          style: const TextStyle(
            fontSize: 11,
            color: NordicColors.textSecondary,
          ),
        ),
      ],
    ),
  );
}
