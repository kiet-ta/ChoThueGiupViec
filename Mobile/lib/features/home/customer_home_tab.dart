import 'package:flutter/material.dart';

import '../../core/theme/nordic_colors.dart';
import '../../core/theme/nordic_typography.dart';
import '../../core/widgets/avatar_initials.dart';
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
/// Every section loads on its own; a failed call hides only its section. No spending chart: no API
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
    return Scaffold(
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.fromLTRB(20, 8, 20, 32),
          children: [
            Text(_today, style: NordicTypography.bodySmall),
            const SizedBox(height: 4),
            Text(
              _firstName == null ? 'Xin chào' : 'Chào $_firstName',
              key: const Key('home-greeting'),
              style: NordicTypography.greeting,
            ),
            const SizedBox(height: 24),
            _nextCard(),
            if (_quotes != null) ..._quickBook(_quotes!),
            if (_favoriteList.isNotEmpty) ..._favoriteRow(),
          ],
        ),
      ),
    );
  }

  Widget _nextCard() {
    if (!_nextLoaded) {
      return Container(
        height: 150,
        decoration: BoxDecoration(
          color: NordicColors.surfaceLow,
          borderRadius: BorderRadius.zero,
        ),
      );
    }
    final next = _next;
    if (next == null) {
      return Material(
        color: NordicColors.surface,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.zero,
          side: const BorderSide(color: NordicColors.border),
        ),
        child: InkWell(
          key: const Key('home-no-order'),
          borderRadius: BorderRadius.zero,
          onTap: _openBooking,
          child: const Padding(
            padding: EdgeInsets.all(16),
            child: Row(
              children: [
                Icon(
                  Icons.cleaning_services_outlined,
                  color: NordicColors.primary,
                ),
                SizedBox(width: 12),
                Expanded(
                  child: Text(
                    'Chưa có ca nào sắp tới',
                    style: NordicTypography.titleSmall,
                  ),
                ),
                Icon(Icons.chevron_right, color: NordicColors.borderVariant),
              ],
            ),
          ),
        ),
      );
    }

    final order = next.order;
    final steps = orderTimeline(order.orderStatus);
    final worker = next.worker;
    const muted = TextStyle(fontSize: 12, color: Color(0xB8FFFFFF));
    return Material(
      color: NordicColors.primary,
      borderRadius: BorderRadius.zero,
      child: InkWell(
        key: const Key('home-next-order'),
        borderRadius: BorderRadius.zero,
        onTap: () => _openOrder(order.orderId),
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: DefaultTextStyle(
            style: const TextStyle(color: Colors.white, fontSize: 14),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(
                      child: Text(
                        'CA SẮP TỚI',
                        style: NordicTypography.caps.copyWith(
                          color: const Color(0xBFFFFFFF),
                        ),
                      ),
                    ),
                    Container(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 10,
                        vertical: 4,
                      ),
                      decoration: BoxDecoration(
                        color: const Color(0x29FFFFFF),
                        borderRadius: BorderRadius.zero,
                      ),
                      child: Text(
                        orderStatusLabel(order.orderStatus),
                        style: const TextStyle(
                          fontSize: 11,
                          fontWeight: FontWeight.w600,
                          color: Colors.white,
                        ),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 16),
                Row(
                  crossAxisAlignment: CrossAxisAlignment.end,
                  children: [
                    Text(
                      next.startLocal ?? shiftLabel(order.shiftCode),
                      style: NordicTypography.numberLarge.copyWith(
                        color: Colors.white,
                      ),
                    ),
                    if (next.endLocal != null) ...[
                      const SizedBox(width: 10),
                      Padding(
                        padding: const EdgeInsets.only(bottom: 4),
                        child: Text('→ ${next.endLocal}', style: muted),
                      ),
                    ],
                  ],
                ),
                const SizedBox(height: 4),
                Text(
                  '${scheduleText(order.scheduledDate, order.shiftCode)} · ${tierLabel(order.serviceTier)}',
                  style: muted,
                ),
                if (steps.length > 1) ...[
                  const SizedBox(height: 16),
                  Row(
                    children: [
                      for (var i = 0; i < steps.length; i++) ...[
                        if (i > 0) const SizedBox(width: 6),
                        Expanded(
                          child: Container(
                            height: 4,
                            decoration: BoxDecoration(
                              color: steps[i].done || steps[i].current
                                  ? Colors.white
                                  : const Color(0x4DFFFFFF),
                              borderRadius: BorderRadius.zero,
                            ),
                          ),
                        ),
                      ],
                    ],
                  ),
                ],
                if (worker != null) ...[
                  const SizedBox(height: 16),
                  Row(
                    children: [
                      AvatarInitials(
                        workerName(worker),
                        size: 32,
                        background: const Color(0x2EFFFFFF),
                        foreground: Colors.white,
                      ),
                      const SizedBox(width: 8),
                      Text(
                        workerName(worker),
                        style: const TextStyle(
                          fontSize: 12,
                          fontWeight: FontWeight.w600,
                          color: Colors.white,
                        ),
                      ),
                      if (ratingText(worker.workerRatingAvg).isNotEmpty) ...[
                        const SizedBox(width: 8),
                        const Icon(
                          Icons.star,
                          size: 16,
                          color: NordicColors.accentAmber,
                        ),
                        const SizedBox(width: 2),
                        Text(
                          ratingText(worker.workerRatingAvg),
                          style: const TextStyle(
                            fontSize: 12,
                            color: Colors.white,
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

  List<Widget> _quickBook(_QuickQuotes q) {
    final any = q.byTier.values.first;
    return [
      SectionTitle(
        'Đặt nhanh',
        trailing:
            '${formatArea(q.address.totalAreaM2)} · ${any.requiredWorkers} thợ',
      ),
      Row(
        children: [
          for (final tier in [ServiceTier.economy, ServiceTier.premium])
            if (q.byTier[tier] != null) ...[
              if (tier == ServiceTier.premium) const SizedBox(width: 12),
              Expanded(child: _quoteTile(tier, q.byTier[tier]!)),
            ],
        ],
      ),
    ];
  }

  Widget _quoteTile(String tier, PriceQuote quote) {
    final price = formatVnd(quote.unitPrice);
    final number = price.substring(0, price.length - 2);
    return Material(
      color: NordicColors.surface,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.zero,
        side: const BorderSide(color: NordicColors.border),
      ),
      child: InkWell(
        key: Key('home-quote-$tier'),
        borderRadius: BorderRadius.zero,
        onTap: _openBooking,
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(tierLabel(tier).toUpperCase(), style: NordicTypography.caps),
              const SizedBox(height: 8),
              Text.rich(
                TextSpan(
                  text: number,
                  children: const [
                    TextSpan(text: ' đ', style: TextStyle(fontSize: 14)),
                  ],
                ),
                style: NordicTypography.numberSmall,
              ),
              const SizedBox(height: 4),
              const Text(
                '/ thợ / ca',
                style: TextStyle(
                  fontSize: 11,
                  color: NordicColors.textSecondary,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  List<Widget> _favoriteRow() => [
    SectionTitle(
      'Thợ yêu thích',
      trailing: 'Tất cả',
      onAction: widget.onShowFavorites,
    ),
    SizedBox(
      height: 72,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        itemCount: _favoriteList.length,
        separatorBuilder: (_, _) => const SizedBox(width: 16),
        itemBuilder: (_, i) {
          final w = _favoriteList[i];
          final words = w.fullName.trim().split(RegExp(r'\s+'));
          return Column(
            children: [
              AvatarInitials(w.fullName),
              const SizedBox(height: 4),
              Text(words.last, style: const TextStyle(fontSize: 11)),
            ],
          );
        },
      ),
    ),
  ];
}
