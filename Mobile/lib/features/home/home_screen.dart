import 'package:flutter/material.dart';

import '../../core/models/user_role.dart';
import '../../core/theme/nordic_colors.dart';
import '../../core/theme/nordic_typography.dart';
import '../../core/widgets/eyebrow_badge.dart';
import '../../core/widgets/nordic_button.dart';
import '../../core/widgets/nordic_card.dart';
import '../../core/widgets/trust_badge.dart';
import '../customers/screens/address_list_screen.dart';
import '../customers/screens/customer_profile_screen.dart';
import '../customers/screens/favorite_workers_screen.dart';
import '../booking/routes.dart';
import '../identity/screens/phone_input_screen.dart';
import '../identity/services/token_storage.dart';
import '../workers/routes.dart';
import 'home_actions.dart';

/// Main Welcome Screen of TỔ ẤM — Nordic Care Mobile application.
/// Strictly conforms to the 390x844 mobile viewport specification.
class HomeScreen extends StatefulWidget {
  const HomeScreen({super.key});

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  AppRole _selectedRole = AppRole.customer;

  /// The signed-in user, or null: the Home shows the actions of this role (MOB-NAV).
  AuthUserView? get _session {
    final storage = TokenStorage();
    final user = storage.currentUser;
    if (!storage.isAuthenticated || user == null) return null;
    return AuthUserView(user.role, user.id);
  }

  @override
  void initState() {
    super.initState();
    final role = _session?.role;
    if (role == SessionRoles.worker) _selectedRole = AppRole.worker;
  }

  /// Back from a pushed screen (the login in particular): show the state of the session as it is now.
  void _refresh() {
    if (!mounted) return;
    setState(() {
      final role = _session?.role;
      if (role == SessionRoles.worker) {
        _selectedRole = AppRole.worker;
      } else if (role == SessionRoles.customer) {
        _selectedRole = AppRole.customer;
      }
    });
  }

  void _open(String route) {
    Navigator.of(context).pushNamed(route).then((_) => _refresh());
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

  Widget _sessionPanel(AuthUserView session) {
    final actions = homeActionsFor(session.role);
    final isWorker = session.role == SessionRoles.worker;
    return NordicCard(
      isHighlighted: true,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(
                isWorker ? Icons.engineering_rounded : Icons.person_rounded,
                color: NordicColors.primary,
              ),
              const SizedBox(width: 8.0),
              Expanded(
                child: Text(
                  isWorker ? 'Thợ #${session.id}' : 'Khách hàng #${session.id}',
                  style: NordicTypography.h3,
                ),
              ),
              TextButton(
                key: const Key('home-sign-out'),
                onPressed: _signOut,
                child: const Text('Đăng xuất'),
              ),
            ],
          ),
          const SizedBox(height: 4.0),
          for (final action in actions)
            // Own Material: the card paints a background that would hide the ink of the tile.
            Material(
              type: MaterialType.transparency,
              child: ListTile(
                key: Key('home-action-${action.route}'),
                contentPadding: EdgeInsets.zero,
                dense: true,
                leading: Icon(action.icon, color: NordicColors.primary),
                title: Text(action.label, style: NordicTypography.labelMedium),
                subtitle: Text(
                  action.subtitle,
                  style: NordicTypography.bodySmall,
                ),
                trailing: const Icon(Icons.chevron_right_rounded),
                onTap: () => _open(action.route),
              ),
            ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(
        title: Row(
          children: [
            Container(
              width: 34.0,
              height: 34.0,
              decoration: BoxDecoration(
                color: NordicColors.primary,
                borderRadius: BorderRadius.circular(10.0),
              ),
              child: const Icon(
                Icons.home_rounded,
                color: Colors.white,
                size: 20.0,
              ),
            ),
            const SizedBox(width: 10.0),
            Text(
              'TỔ ẤM',
              style: NordicTypography.h2.copyWith(
                letterSpacing: 0.5,
                color: NordicColors.primary,
              ),
            ),
            const SizedBox(width: 6.0),
            Text(
              'Nordic Care',
              style: NordicTypography.bodySmall.copyWith(
                color: NordicColors.secondaryAccent,
                fontWeight: FontWeight.w600,
              ),
            ),
          ],
        ),
      ),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: ListView(
            padding: const EdgeInsets.symmetric(
              horizontal: 16.0,
              vertical: 12.0,
            ),
            children: [
              // Eyebrow badge
              const Align(
                alignment: Alignment.centerLeft,
                child: EyebrowBadge(text: 'Tiêu Chuẩn Dọn Dẹp Bắc Âu'),
              ),
              const SizedBox(height: 12.0),

              // Hero headline
              RichText(
                text: TextSpan(
                  style: NordicTypography.h1,
                  children: [
                    const TextSpan(text: 'Thảnh thơi trở về '),
                    TextSpan(
                      text: 'tổ ấm',
                      style: NordicTypography.h1.copyWith(
                        color: NordicColors.primary,
                      ),
                    ),
                    const TextSpan(text: ' sạch trong lành.'),
                  ],
                ),
              ),
              const SizedBox(height: 8.0),
              const Text(
                'Dịch vụ giúp việc tiêu chuẩn vệ sinh Bắc Âu: minh bạch, đúng giờ, an tâm tuyệt đối.',
                style: NordicTypography.bodyRegular,
              ),
              const SizedBox(height: 16.0),

              // 4 Trust badges wrap
              const Wrap(
                spacing: 8.0,
                runSpacing: 8.0,
                children: [
                  TrustBadge(text: 'Thợ mang 100% đồ nghề'),
                  TrustBadge(text: 'Nghiệm thu tại chỗ & dọn lại'),
                  TrustBadge(text: 'Bảo hiểm an tâm 50 triệu'),
                  TrustBadge(text: 'Minh bạch - Không phụ phí'),
                ],
              ),
              const SizedBox(height: 24.0),

              if (_session != null) ...[
                _sessionPanel(_session!),
                const SizedBox(height: 24.0),
              ],

              // Role selector toggle
              Text(
                'Chọn vai trò trải nghiệm:',
                style: NordicTypography.labelMedium,
              ),
              const SizedBox(height: 8.0),
              Container(
                padding: const EdgeInsets.all(4.0),
                decoration: BoxDecoration(
                  color: NordicColors.surfaceSubtle,
                  borderRadius: BorderRadius.circular(9999.0),
                  border: Border.all(color: NordicColors.border),
                ),
                child: Row(
                  children: [
                    Expanded(
                      child: GestureDetector(
                        onTap: () =>
                            setState(() => _selectedRole = AppRole.customer),
                        child: AnimatedContainer(
                          duration: const Duration(milliseconds: 150),
                          padding: const EdgeInsets.symmetric(vertical: 10.0),
                          decoration: BoxDecoration(
                            color: _selectedRole == AppRole.customer
                                ? NordicColors.surface
                                : Colors.transparent,
                            borderRadius: BorderRadius.circular(9999.0),
                            boxShadow: _selectedRole == AppRole.customer
                                ? [
                                    BoxShadow(
                                      color: Colors.black.withValues(
                                        alpha: 0.05,
                                      ),
                                      blurRadius: 4.0,
                                      offset: const Offset(0, 1),
                                    ),
                                  ]
                                : null,
                          ),
                          child: Center(
                            child: Text(
                              'Khách Hàng',
                              style: TextStyle(
                                fontSize: 13.0,
                                fontWeight: _selectedRole == AppRole.customer
                                    ? FontWeight.w700
                                    : FontWeight.w500,
                                color: _selectedRole == AppRole.customer
                                    ? NordicColors.primary
                                    : NordicColors.textBody,
                              ),
                            ),
                          ),
                        ),
                      ),
                    ),
                    Expanded(
                      child: GestureDetector(
                        onTap: () =>
                            setState(() => _selectedRole = AppRole.worker),
                        child: AnimatedContainer(
                          duration: const Duration(milliseconds: 150),
                          padding: const EdgeInsets.symmetric(vertical: 10.0),
                          decoration: BoxDecoration(
                            color: _selectedRole == AppRole.worker
                                ? NordicColors.surface
                                : Colors.transparent,
                            borderRadius: BorderRadius.circular(9999.0),
                            boxShadow: _selectedRole == AppRole.worker
                                ? [
                                    BoxShadow(
                                      color: Colors.black.withValues(
                                        alpha: 0.05,
                                      ),
                                      blurRadius: 4.0,
                                      offset: const Offset(0, 1),
                                    ),
                                  ]
                                : null,
                          ),
                          child: Center(
                            child: Text(
                              'Chuyên Viên / Thợ',
                              style: TextStyle(
                                fontSize: 13.0,
                                fontWeight: _selectedRole == AppRole.worker
                                    ? FontWeight.w700
                                    : FontWeight.w500,
                                color: _selectedRole == AppRole.worker
                                    ? NordicColors.primary
                                    : NordicColors.textBody,
                              ),
                            ),
                          ),
                        ),
                      ),
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 20.0),

              // Mode active container
              if (_selectedRole == AppRole.customer) ...[
                // Customer section
                NordicCard(
                  isHighlighted: true,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Row(
                        children: [
                          const Icon(
                            Icons.star_rounded,
                            color: NordicColors.accentAmber,
                            size: 20.0,
                          ),
                          const SizedBox(width: 4.0),
                          Expanded(
                            child: Text(
                              'GÓI ĐƯỢC CHỌN NHIỀU NHẤT',
                              style: NordicTypography.labelSmall,
                            ),
                          ),
                        ],
                      ),
                      const SizedBox(height: 6.0),
                      Text('Căn Hộ Chung Cư', style: NordicTypography.h3),
                      const SizedBox(height: 4.0),
                      Text('240.000 đ', style: NordicTypography.priceHighlight),
                      const Text(
                        'Ca 3 giờ tiêu chuẩn • Hút bụi khe hẹp sofa, gầm giường',
                        style: NordicTypography.bodySmall,
                      ),
                      const SizedBox(height: 14.0),
                      NordicButton(
                        label: 'Đặt Ca Ngay',
                        width: double.infinity,
                        onPressed: () {
                          final route = routeWhenSignedIn(
                            sessionRole: _session?.role,
                            needed: SessionRoles.customer,
                            route: BookingRoutes.booking,
                          );
                          if (route != null) {
                            _open(route);
                          } else {
                            _openLogin(AppRole.customer);
                          }
                        },
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 12.0),
                NordicCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        'Nhà Phố Liền Kề & Biệt Thự',
                        style: NordicTypography.h3,
                      ),
                      const SizedBox(height: 4.0),
                      Text('360.000 đ', style: NordicTypography.priceHighlight),
                      const Text(
                        'Ca 4 giờ • Khử khuẩn sinh học và nghiệm thu quang học',
                        style: NordicTypography.bodySmall,
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 12.0),
                NordicCard(
                  child: Row(
                    children: [
                      Container(
                        width: 40.0,
                        height: 40.0,
                        decoration: BoxDecoration(
                          color: NordicColors.primary.withValues(alpha: 0.1),
                          borderRadius: BorderRadius.circular(10.0),
                        ),
                        child: const Icon(
                          Icons.location_on_rounded,
                          color: NordicColors.primary,
                          size: 22.0,
                        ),
                      ),
                      const SizedBox(width: 12.0),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              'Sổ Địa Chỉ Của Bạn',
                              style: NordicTypography.h3,
                            ),
                            Text(
                              'Quản lý địa chỉ, loại nhà & toạ độ GPS',
                              style: NordicTypography.bodySmall,
                            ),
                          ],
                        ),
                      ),
                      NordicButton(
                        label: 'Quản Lý',
                        variant: NordicButtonVariant.secondary,
                        onPressed: () {
                          Navigator.push(
                            context,
                            MaterialPageRoute(
                              builder: (context) => const AddressListScreen(),
                            ),
                          );
                        },
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 12.0),
                NordicCard(
                  child: Row(
                    children: [
                      Container(
                        width: 40.0,
                        height: 40.0,
                        decoration: BoxDecoration(
                          color: NordicColors.primary.withValues(alpha: 0.1),
                          borderRadius: BorderRadius.circular(10.0),
                        ),
                        child: const Icon(
                          Icons.favorite_rounded,
                          color: NordicColors.primary,
                          size: 22.0,
                        ),
                      ),
                      const SizedBox(width: 12.0),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              'Thợ Quen Yêu Thích',
                              style: NordicTypography.h3,
                            ),
                            Text(
                              'Chuyên viên uy tín bạn đã lưu để đặt lại',
                              style: NordicTypography.bodySmall,
                            ),
                          ],
                        ),
                      ),
                      NordicButton(
                        label: 'Danh Sách',
                        variant: NordicButtonVariant.secondary,
                        onPressed: () {
                          Navigator.push(
                            context,
                            MaterialPageRoute(
                              builder: (context) =>
                                  const FavoriteWorkersScreen(),
                            ),
                          );
                        },
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 12.0),
                NordicCard(
                  child: Row(
                    children: [
                      Container(
                        width: 40.0,
                        height: 40.0,
                        decoration: BoxDecoration(
                          color: NordicColors.primary.withValues(alpha: 0.1),
                          borderRadius: BorderRadius.circular(10.0),
                        ),
                        child: const Icon(
                          Icons.person_outline_rounded,
                          color: NordicColors.primary,
                          size: 22.0,
                        ),
                      ),
                      const SizedBox(width: 12.0),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text('Hồ Sơ Cá Nhân', style: NordicTypography.h3),
                            Text(
                              'Thông tin tài khoản, điểm tin cậy & email',
                              style: NordicTypography.bodySmall,
                            ),
                          ],
                        ),
                      ),
                      NordicButton(
                        label: 'Xem & Sửa',
                        variant: NordicButtonVariant.secondary,
                        onPressed: () {
                          Navigator.push(
                            context,
                            MaterialPageRoute(
                              builder: (context) =>
                                  const CustomerProfileScreen(),
                            ),
                          );
                        },
                      ),
                    ],
                  ),
                ),
              ] else ...[
                // Worker section
                NordicCard(
                  isHighlighted: true,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Row(
                        children: [
                          const Icon(
                            Icons.work_history_rounded,
                            color: NordicColors.primary,
                          ),
                          const SizedBox(width: 8.0),
                          Expanded(
                            child: Text(
                              'Bàn Điều Phối Ca Làm',
                              style: NordicTypography.h3,
                            ),
                          ),
                        ],
                      ),
                      const SizedBox(height: 8.0),
                      const Text(
                        'Nhận cuốc 30s đếm ngược, định vị GPS hiện trường và nghiệm thu minh bạch.',
                        style: NordicTypography.bodyRegular,
                      ),
                      const SizedBox(height: 14.0),
                      NordicButton(
                        label: 'Vào Trực Ca',
                        width: double.infinity,
                        onPressed: () {
                          final route = routeWhenSignedIn(
                            sessionRole: _session?.role,
                            needed: SessionRoles.worker,
                            route: WorkerRoutes.slots,
                          );
                          if (route != null) {
                            _open(route);
                          } else {
                            _openLogin(AppRole.worker);
                          }
                        },
                      ),
                    ],
                  ),
                ),
              ],

              const SizedBox(height: 24.0),

              // High-contrast Dark Green Anchor: Trụ cột an tâm
              Container(
                padding: const EdgeInsets.all(20.0),
                decoration: BoxDecoration(
                  color: NordicColors.contrastDark,
                  borderRadius: BorderRadius.circular(20.0),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      'TRỤ CỘT AN TÂM',
                      style: NordicTypography.labelSmall.copyWith(
                        color: NordicColors.secondaryAccent,
                      ),
                    ),
                    const SizedBox(height: 6.0),
                    Text(
                      'Chăm sóc tổ ấm theo tiêu chuẩn an toàn cao nhất',
                      style: NordicTypography.h3.copyWith(color: Colors.white),
                    ),
                    const SizedBox(height: 16.0),
                    _buildDarkPillar(
                      '01',
                      'Lý lịch & tay nghề',
                      '100% chuyên viên xác thực eKYC và đào tạo.',
                    ),
                    const SizedBox(height: 12.0),
                    _buildDarkPillar(
                      '02',
                      'Đúng giờ & chuẩn vị trí',
                      'Kiểm tra GPS hiện trường sai số ≤ 100m.',
                    ),
                    const SizedBox(height: 12.0),
                    _buildDarkPillar(
                      '03',
                      'Nghiệm thu quang học',
                      'Chụp ảnh trước/sau và đánh giá hài lòng.',
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 24.0),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildDarkPillar(String number, String title, String desc) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          number,
          style: const TextStyle(
            fontSize: 16.0,
            fontWeight: FontWeight.w700,
            color: NordicColors.secondaryAccent,
          ),
        ),
        const SizedBox(width: 12.0),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                title,
                style: const TextStyle(
                  fontSize: 14.0,
                  fontWeight: FontWeight.w600,
                  color: Colors.white,
                ),
              ),
              const SizedBox(height: 2.0),
              Text(
                desc,
                style: const TextStyle(
                  fontSize: 12.0,
                  color: Color(0xFFC2C8BF),
                  height: 1.4,
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

/// The role and id of the signed-in user, as the Home reads them.
class AuthUserView {
  final String role;
  final int id;

  const AuthUserView(this.role, this.id);
}
