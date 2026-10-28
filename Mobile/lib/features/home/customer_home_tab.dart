import 'package:flutter/material.dart';

import '../../core/theme/nordic_colors.dart';
import '../../core/theme/nordic_radius.dart';
import '../../core/theme/nordic_spacing.dart';
import '../../core/theme/nordic_typography.dart';
import '../../core/widgets/avatar_initials.dart';
import '../../core/widgets/nordic_card.dart';
import '../../core/widgets/pressable.dart';
import '../../core/widgets/section_title.dart';
import '../booking/logic/booking_logic.dart';
import '../booking/logic/order_logic.dart';
import '../booking/models/booking_models.dart';
import '../booking/routes.dart';
import '../booking/services/booking_service.dart';
import '../booking/services/order_service.dart';
import '../customers/models/favorite_worker.dart';
import '../customers/services/customer_profile_service.dart';
import '../customers/services/favorite_worker_service.dart';

/// The customer's Trang chủ tab (ui-templates `customer/home.html`): greeting, the next booking, quick
/// booking with the server's prices for the default address, and favourite workers.
/// MOB-UI-14 (`Mobile/DESIGN-SYSTEM.md`): a photo card is the main action, every card presses,
/// plain words. Every section loads on its own; a failed call hides only its section. No spending chart: no API
/// returns a customer's spending (README "needs API").
class CustomerHomeTab extends StatefulWidget {
  final IOrderService? orders;
  final IBookingService? booking;
  final IBookingAddressSource? addresses;
  final FavoriteWorkerService? favorites;
  final CustomerProfileService? profile;

  /// Now, for the date line; tests pass a fixed instant.
  final DateTime Function()? clock;
  final VoidCallback? onShowFavorites;

  const CustomerHomeTab({
    super.key,
    this.orders,
    this.booking,
    this.addresses,
    this.favorites,
    this.profile,
    this.clock,
    this.onShowFavorites,
  });

  @override
  State<CustomerHomeTab> createState() => _CustomerHomeTabState();
}

/// The next booking as the Home shows it.
class _NextOrder {
  final OrderSummary order;
  final String? startLocal;
  final String? endLocal;
  final ProgressAssignment? worker;

  const _NextOrder(this.order, this.startLocal, this.endLocal, this.worker);
}

class _QuickQuotes {
  final BookingAddress address;
  final Map<String, PriceQuote> byTier;

  const _QuickQuotes(this.address, this.byTier);
}

class _CustomerHomeTabState extends State<CustomerHomeTab> {
  late final IOrderService _orders = widget.orders ?? OrderService();
  late final IBookingService _booking = widget.booking ?? BookingService();
  late final IBookingAddressSource _addresses =
      widget.addresses ?? CustomerAddressBookSource();
  late final FavoriteWorkerService _favorites =
      widget.favorites ?? FavoriteWorkerService();
  late final CustomerProfileService _profile =
      widget.profile ?? CustomerProfileService();

  String? _firstName;
  _NextOrder? _next;
  bool _nextLoaded = false;
  _QuickQuotes? _quotes;
  List<FavoriteWorker> _favoriteList = const [];

  @override
  void initState() {
    super.initState();
    _loadName();
    _loadNext();
    _loadQuotes();
    _loadFavorites();
  }

  Future<void> _loadName() async {
    try {
      final p = await _profile.getProfile();
      final words = p.fullName.trim().split(RegExp(r'\s+'));
      if (!mounted || words.first.isEmpty) return;
      setState(() => _firstName = words.last);
    } catch (_) {
      // The greeting stays generic.
    }
  }

  Future<void> _loadNext() async {
    try {
      final page = await _orders.listOrders();
      final open = page.items.where(
        (o) =>
            o.orderStatus != OrderStatus.completed &&
            o.orderStatus != OrderStatus.cancelled,
      );
      if (open.isEmpty) {
        if (mounted) setState(() => _nextLoaded = true);
        return;
      }
      final order = open.first;
      String? start;
      String? end;
      ProgressAssignment? worker;
      try {
        final options = await _booking.getOptions();
        final shift = options.shifts
            .where((s) => s.shiftCode == order.shiftCode)
            .firstOrNull;
        start = shift?.startLocal;
        end = shift?.endLocal;
      } catch (_) {}
      try {
        final progress = await _orders.getProgress(order.orderId);
        worker = progress.assignments.firstOrNull;
      } catch (_) {}
      if (!mounted) return;
      setState(() {
        _next = _NextOrder(order, start, end, worker);
        _nextLoaded = true;
      });
    } catch (_) {
      if (mounted) setState(() => _nextLoaded = true);
    }
  }

  Future<void> _loadQuotes() async {
    try {
      final list = await _addresses.getAddresses();
      if (list.isEmpty) return;
      final address = list.firstWhere(
        (a) => a.isDefault,
        orElse: () => list.first,
      );
      final byTier = <String, PriceQuote>{};
      for (final tier in [ServiceTier.economy, ServiceTier.premium]) {
        try {
          byTier[tier] = await _booking.getQuote(
            addressId: address.addressId,
            serviceTier: tier,
          );
        } catch (_) {}
      }
      if (!mounted || byTier.isEmpty) return;
      setState(() => _quotes = _QuickQuotes(address, byTier));
    } catch (_) {
      // No quick booking; the centre button still opens the booking form.
    }
  }

  Future<void> _loadFavorites() async {
    try {
      final list = await _favorites.getFavoriteWorkers();
      if (mounted) setState(() => _favoriteList = list);
    } catch (_) {}
  }

  void _openBooking() => Navigator.of(context).pushNamed(BookingRoutes.booking);

  void _openOrder(int orderId) => Navigator.of(context)
      .pushNamed(
        BookingRoutes.bookingDetail,
        arguments: OrderDetailArgs(orderId: orderId),
      )
      .then((_) {
        if (mounted) _loadNext();
      });

  static const _weekdayNames = [
    'Thứ Hai',
    'Thứ Ba',
    'Thứ Tư',
    'Thứ Năm',
    'Thứ Sáu',
    'Thứ Bảy',
    'Chủ Nhật',
  ];

  String get _today {
    final day = hoChiMinhDay((widget.clock ?? DateTime.now)());
    final dd = day.day.toString().padLeft(2, '0');
    final mm = day.month.toString().padLeft(2, '0');
    return '${_weekdayNames[day.weekday - 1]}, $dd/$mm';
  }

  @override
  Widget build(BuildContext context) {
    // The shell's translucent bar sits over the bottom of this tab: keep the end clear of it.
    final bottom = MediaQuery.paddingOf(context).bottom;
    return Scaffold(
      body: SafeArea(
        bottom: false,
        child: ListView(
          padding: EdgeInsets.fromLTRB(
            NordicSpace.gutter,
            NordicSpace.md,
            NordicSpace.gutter,
            NordicSpace.xl + bottom,
          ),
          children: [
            Text(
              _today,
              style: NordicTypography.bodyRegular.copyWith(
                color: NordicColors.textSecondary,
              ),
            ),
            const SizedBox(height: NordicSpace.xxs),
            Text(
              _firstName == null ? 'Xin chào' : 'Chào $_firstName',
              key: const Key('home-greeting'),
              style: NordicTypography.greeting,
            ),
            const SizedBox(height: NordicSpace.lg),
            _nextCard(),
            const SizedBox(height: NordicSpace.sm),
            _bookBanner(),
            if (_quotes != null) ..._quickBook(_quotes!),
            if (_favoriteList.isNotEmpty) ..._favoriteRow(),
          ],
        ),
      ),
    );
  }

  /// The next booking: the one big time, its day, progress and helper, on the primary colour.
  Widget _nextCard() {
    if (!_nextLoaded) {
      return Container(
        height: 168,
        decoration: const BoxDecoration(
          color: NordicColors.surfaceLow,
          borderRadius: NordicRadius.cardAll,
        ),
      );
    }
    final next = _next;
    if (next == null) {
      return NordicCard(
        key: const Key('home-no-order'),
        onTap: _openBooking,
        child: Row(
          children: [
            Container(
              width: 44,
              height: 44,
              decoration: const BoxDecoration(
                color: NordicColors.primaryFixed,
                borderRadius: NordicRadius.tileAll,
              ),
              child: const Icon(
                Icons.event_available_outlined,
                color: NordicColors.primary,
              ),
            ),
            const SizedBox(width: NordicSpace.sm),
            const Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    'Chưa có ca nào sắp tới',
                    style: NordicTypography.titleSmall,
                  ),
                  SizedBox(height: 2),
                  Text(
                    'Bấm để đặt ca dọn nhà.',
                    style: NordicTypography.bodySmall,
                  ),
                ],
              ),
            ),
            const Icon(Icons.chevron_right, color: NordicColors.borderVariant),
          ],
        ),
      );
    }

    final order = next.order;
    final steps = orderTimeline(order.orderStatus);
    final worker = next.worker;
    final soft = NordicColors.textInverse.withValues(alpha: 0.78);
    return Pressable(
      child: Material(
        color: NordicColors.primary,
        borderRadius: NordicRadius.cardAll,
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          key: const Key('home-next-order'),
          onTap: () => _openOrder(order.orderId),
          child: Padding(
            padding: const EdgeInsets.all(NordicSpace.gutter),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(
                      child: Text(
                        'Ca sắp tới',
                        style: NordicTypography.titleSmall.copyWith(
                          color: soft,
                        ),
                      ),
                    ),
                    Container(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 10,
                        vertical: NordicSpace.xxs,
                      ),
                      decoration: BoxDecoration(
                        color: NordicColors.textInverse.withValues(alpha: 0.16),
                        borderRadius: NordicRadius.pill,
                      ),
                      child: Text(
                        orderStatusLabel(order.orderStatus),
                        style: const TextStyle(
                          fontSize: 12,
                          fontWeight: FontWeight.w600,
                          color: NordicColors.textInverse,
                        ),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: NordicSpace.sm),
                Row(
                  crossAxisAlignment: CrossAxisAlignment.end,
                  children: [
                    Text(
                      next.startLocal ?? shiftLabel(order.shiftCode),
                      style: NordicTypography.numberLarge.copyWith(
                        color: NordicColors.textInverse,
                      ),
                    ),
                    if (next.endLocal != null) ...[
                      const SizedBox(width: 10),
                      Padding(
                        padding: const EdgeInsets.only(bottom: 4),
                        child: Text(
                          'đến ${next.endLocal}',
                          style: NordicTypography.bodyRegular.copyWith(
                            color: soft,
                          ),
                        ),
                      ),
                    ],
                  ],
                ),
                const SizedBox(height: NordicSpace.xxs),
                Text(
                  '${scheduleText(order.scheduledDate, order.shiftCode)} · ${tierLabel(order.serviceTier)}',
                  style: NordicTypography.bodyRegular.copyWith(color: soft),
                ),
                if (steps.length > 1) ...[
                  const SizedBox(height: NordicSpace.md),
                  Row(
                    children: [
                      for (var i = 0; i < steps.length; i++) ...[
                        if (i > 0) const SizedBox(width: 6),
                        Expanded(
                          child: Container(
                            height: 4,
                            decoration: BoxDecoration(
                              color: steps[i].done || steps[i].current
                                  ? NordicColors.textInverse
                                  : NordicColors.textInverse.withValues(
                                      alpha: 0.3,
                                    ),
                              borderRadius: NordicRadius.pill,
                            ),
                          ),
                        ),
                      ],
                    ],
                  ),
                ],
                if (worker != null) ...[
                  const SizedBox(height: NordicSpace.md),
                  Row(
                    children: [
                      AvatarInitials(
                        workerName(worker),
                        size: 36,
                        background: NordicColors.textInverse.withValues(
                          alpha: 0.18,
                        ),
                        foreground: NordicColors.textInverse,
                      ),
                      const SizedBox(width: NordicSpace.sm),
                      Expanded(
                        child: Text(
                          workerName(worker),
                          style: NordicTypography.titleSmall.copyWith(
                            color: NordicColors.textInverse,
                          ),
                        ),
                      ),
                      if (ratingText(worker.workerRatingAvg).isNotEmpty) ...[
                        const Icon(
                          Icons.star,
                          size: 16,
                          color: NordicColors.accentAmber,
                        ),
                        const SizedBox(width: 2),
                        Text(
                          ratingText(worker.workerRatingAvg),
                          style: const TextStyle(
                            fontSize: 13,
                            fontWeight: FontWeight.w600,
                            color: NordicColors.textInverse,
                          ),
                        ),
                      ],
                    ],
                  ),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }

  /// A photo card that opens the booking form: the one main action of the Home.
  Widget _bookBanner() {
    return Pressable(
      child: Semantics(
        button: true,
        label: 'Đặt ca dọn nhà',
        child: Material(
          borderRadius: NordicRadius.cardAll,
          clipBehavior: Clip.antiAlias,
          child: InkWell(
            key: const Key('home-book-banner'),
            onTap: _openBooking,
            child: SizedBox(
              height: 128,
              child: Stack(
                fit: StackFit.expand,
                children: [
                  ExcludeSemantics(
                    child: Image.asset(
                      'assets/images/welcome/mop-living-room.jpg',
                      fit: BoxFit.cover,
                      alignment: const Alignment(0, -0.2),
                    ),
                  ),
                  DecoratedBox(
                    decoration: BoxDecoration(
                      gradient: LinearGradient(
                        begin: Alignment.centerLeft,
                        end: Alignment.centerRight,
                        colors: [
                          NordicColors.contrastDark.withValues(alpha: 0.82),
                          NordicColors.contrastDark.withValues(alpha: 0.1),
                        ],
                      ),
                    ),
                  ),
                  Padding(
                    padding: const EdgeInsets.all(NordicSpace.gutter),
                    child: Row(
                      children: [
                        Expanded(
                          child: ExcludeSemantics(
                            child: Column(
                              mainAxisAlignment: MainAxisAlignment.center,
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(
                                  'Đặt ca dọn nhà',
                                  style: NordicTypography.titleMedium.copyWith(
                                    color: NordicColors.textInverse,
                                  ),
                                ),
                                const SizedBox(height: NordicSpace.xxs),
                                Text(
                                  'Chọn ngày, ca và gói phù hợp.',
                                  style: NordicTypography.bodyRegular.copyWith(
                                    color: NordicColors.textInverse.withValues(
                                      alpha: 0.85,
                                    ),
                                  ),
                                ),
                              ],
                            ),
                          ),
                        ),
                        Container(
                          width: 40,
                          height: 40,
                          decoration: const BoxDecoration(
                            color: NordicColors.textInverse,
                            shape: BoxShape.circle,
                          ),
                          child: const Icon(
                            Icons.arrow_forward,
                            color: NordicColors.primary,
                          ),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  List<Widget> _quickBook(_QuickQuotes q) {
    final any = q.byTier.values.first;
    return [
      SectionTitle(
        'Đặt nhanh',
        trailing:
            '${formatArea(q.address.totalAreaM2)} · ${any.requiredWorkers} người',
      ),
      Row(
        children: [
          for (final tier in [ServiceTier.economy, ServiceTier.premium])
            if (q.byTier[tier] != null) ...[
              if (tier == ServiceTier.premium)
                const SizedBox(width: NordicSpace.sm),
              Expanded(child: _quoteTile(tier, q.byTier[tier]!)),
            ],
        ],
      ),
    ];
  }

  Widget _quoteTile(String tier, PriceQuote quote) {
    final price = formatVnd(quote.unitPrice);
    final number = price.substring(0, price.length - 2);
    return NordicCard(
      key: Key('home-quote-$tier'),
      onTap: _openBooking,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(tierLabel(tier), style: NordicTypography.titleSmall),
          const SizedBox(height: NordicSpace.xs),
          Text.rich(
            TextSpan(
              text: number,
              children: const [
                TextSpan(text: ' đ', style: TextStyle(fontSize: 14)),
              ],
            ),
            style: NordicTypography.numberSmall,
          ),
          const SizedBox(height: NordicSpace.xxs),
          const Text('mỗi người, mỗi ca', style: NordicTypography.bodySmall),
        ],
      ),
    );
  }

  List<Widget> _favoriteRow() => [
    SectionTitle(
      'Người giúp việc yêu thích',
      trailing: 'Tất cả',
      onAction: widget.onShowFavorites,
    ),
    SizedBox(
      height: 84,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        itemCount: _favoriteList.length,
        separatorBuilder: (_, _) => const SizedBox(width: NordicSpace.md),
        itemBuilder: (_, i) {
          final w = _favoriteList[i];
          final words = w.fullName.trim().split(RegExp(r'\s+'));
          return Pressable(
            child: InkWell(
              borderRadius: NordicRadius.tileAll,
              onTap: widget.onShowFavorites,
              child: Padding(
                padding: const EdgeInsets.all(NordicSpace.xxs),
                child: Column(
                  children: [
                    AvatarInitials(w.fullName, size: 52),
                    const SizedBox(height: NordicSpace.xxs),
                    Text(words.last, style: NordicTypography.bodySmall),
                  ],
                ),
              ),
            ),
          );
        },
      ),
    ),
  ];
}
