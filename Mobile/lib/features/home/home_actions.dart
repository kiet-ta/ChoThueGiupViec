import 'package:flutter/material.dart';

import '../agencies/routes.dart';
import '../booking/routes.dart';
import '../customers/routes.dart';
import '../dispatch/routes.dart';
import '../payouts/routes.dart';
import '../workers/routes.dart';

/// One entry of the signed-in Home: a label, a line of help and the named route it opens.
class HomeAction {
  final String label;
  final String subtitle;
  final String route;
  final IconData icon;

  const HomeAction({
    required this.label,
    required this.subtitle,
    required this.route,
    required this.icon,
  });
}

/// Role names as the login answer carries them (`AuthUser.role`).
class SessionRoles {
  SessionRoles._();

  static const String customer = 'Customer';
  static const String worker = 'Worker';
}

/// What a signed-in user can open from Home (MOB-NAV). Before this the screens below were finished but nothing opened them.
/// The login answer does not say whether a worker is an agency's staff or a freelancer, so a worker sees the common entries and the
/// company schedule (empty for a freelancer).
List<HomeAction> homeActionsFor(String? role) {
  switch (role) {
    case SessionRoles.customer:
      return const [
        HomeAction(
          label: 'Đặt ca',
          subtitle: 'Chọn địa chỉ, ngày, ca và gói dịch vụ',
          route: BookingRoutes.booking,
          icon: Icons.add_task_rounded,
        ),
        HomeAction(
          label: 'Đơn của tôi',
          subtitle: 'Theo dõi, thanh toán, nghiệm thu',
          route: BookingRoutes.orders,
          icon: Icons.receipt_long_rounded,
        ),
        HomeAction(
          label: 'Sổ địa chỉ',
          subtitle: 'Địa chỉ nhà và vị trí',
          route: CustomerRoutes.addressList,
          icon: Icons.place_rounded,
        ),
        HomeAction(
          label: 'Hồ sơ',
          subtitle: 'Thông tin tài khoản',
          route: CustomerRoutes.customerProfile,
          icon: Icons.person_rounded,
        ),
        HomeAction(
          label: 'Thợ yêu thích',
          subtitle: 'Danh sách thợ đã lưu',
          route: CustomerRoutes.favoriteWorkers,
          icon: Icons.favorite_rounded,
        ),
      ];
    case SessionRoles.worker:
      return const [
        HomeAction(
          label: 'Xác minh danh tính (eKYC)',
          subtitle: 'Gửi ảnh CCCD và ảnh chân dung',
          route: WorkerRoutes.ekyc,
          icon: Icons.verified_user_rounded,
        ),
        HomeAction(
          label: 'Lịch rảnh',
          subtitle: 'Bật ca bạn có thể nhận việc',
          route: WorkerRoutes.slots,
          icon: Icons.event_available_rounded,
        ),
        HomeAction(
          label: 'Nhận việc',
          subtitle: 'Cuốc đang chờ, đếm ngược 30 giây',
          route: DispatchRoutes.offers,
          icon: Icons.notifications_active_rounded,
        ),
        HomeAction(
          label: 'Thu nhập',
          subtitle: 'Thu nhập tháng và lịch sử chuyển khoản',
          route: PayoutRoutes.payouts,
          icon: Icons.payments_rounded,
        ),
        HomeAction(
          label: 'Hồ sơ & chất lượng',
          subtitle: 'Điểm đánh giá và hồ sơ thợ',
          route: WorkerRoutes.profile,
          icon: Icons.badge_rounded,
        ),
        HomeAction(
          label: 'Lịch ca của công ty',
          subtitle: 'Dành cho thợ của công ty cung cấp nhân sự',
          route: AgencyRoutes.roster,
          icon: Icons.groups_rounded,
        ),
      ];
    default:
      return const [];
  }
}

/// The route a Home button opens when the user is signed in with [needed], or null when the login must come first.
String? routeWhenSignedIn({
  required String? sessionRole,
  required String needed,
  required String route,
}) => sessionRole == needed ? route : null;
