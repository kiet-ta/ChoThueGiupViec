import 'package:flutter/material.dart';

import '../../core/theme/nordic_colors.dart';
import '../../core/theme/nordic_elevation.dart';
import '../../core/theme/nordic_radius.dart';
import '../../core/theme/nordic_spacing.dart';
import '../../core/theme/nordic_typography.dart';
import '../../core/widgets/avatar_initials.dart';
import '../../core/widgets/nordic_button.dart';
import '../../core/widgets/nordic_card.dart';
import '../../core/widgets/pressable.dart';
import '../../core/widgets/section_title.dart';
import '../booking/logic/booking_logic.dart';
import '../booking/logic/order_logic.dart';
import '../booking/models/booking_models.dart';
import '../booking/routes.dart';
import '../booking/screens/booking_screen.dart';
import '../booking/services/booking_service.dart';
import '../booking/services/order_service.dart';
import '../customers/models/favorite_worker.dart';
import '../customers/routes.dart';
import '../customers/services/customer_profile_service.dart';
import '../customers/services/favorite_worker_service.dart';

/// The customer's Trang chủ tab (redesign 2026-10-30, `Mobile/DESIGN-SYSTEM.md` §5 "Home"):
/// a deep-green header (date, greeting, default address, the next booking), a raised booking card
/// that overlaps the header (the two segments with the server's prices for the default address and
/// one "Đặt ca" button), then the favourite helpers as cards with "Đặt lại".
/// Every section loads on its own; a failed call hides only its section. The device computes no
/// money: every price is the server's quote. No spending chart, no cooking service, no
/// notifications: no API feeds them.
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

  /// How far the booking card rises over the dark header.
  static const double _overlap = 64;

  String? _firstName;
  _NextOrder? _next;
  bool _nextLoaded = false;
  String? _nextError;
  BookingOptions? _options;
  _QuickQuotes? _quotes;
  String _tier = ServiceTier.economy;
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
    if (mounted) {
      setState(() {
        _nextLoaded = false;
        _nextError = null;
        _next = null;
      });
    }
    try {
      final page = await _orders.listOrders();
      final options = await _booking.getOptions();
      if (mounted) setState(() => _options = options);
      final now = (widget.clock ?? DateTime.now)().toUtc().add(
        const Duration(hours: 7),
      );
      final localNow = DateTime.utc(
        now.year,
        now.month,
        now.day,
        now.hour,
        now.minute,
      );
      final open = page.items.where((o) {
        final startsAt = _startsAt(o, options);
        return o.orderStatus != OrderStatus.completed &&
            o.orderStatus != OrderStatus.cancelled &&
            startsAt != null &&
            !startsAt.isBefore(localNow);
      }).toList();
      if (open.isEmpty) {
        if (mounted) setState(() => _nextLoaded = true);
        return;
      }
      open.sort(
        (a, b) => _startsAt(a, options)!.compareTo(_startsAt(b, options)!),
      );
      final order = open.first;
      final shift = options.shifts
          .where((s) => s.shiftCode == order.shiftCode)
          .firstOrNull;
      ProgressAssignment? worker;
      try {
        final progress = await _orders.getProgress(order.orderId);
        worker = progress.assignments.firstOrNull;
      } catch (_) {}
      if (!mounted) return;
      setState(() {
        _next = _NextOrder(order, shift?.startLocal, shift?.endLocal, worker);
        _nextLoaded = true;
      });
    } catch (_) {
      if (mounted) {
        setState(() {
          _nextLoaded = true;
          _nextError = 'Chưa tải được lịch ca. Thử lại nhé.';
        });
      }
    }
  }

  /// A shift is a local Vietnam date plus the start time supplied by Booking.
  /// Orders from an earlier shift must never be promoted as the next appointment.
  static DateTime? _startsAt(OrderSummary order, BookingOptions options) {
    final date = DateTime.tryParse(order.scheduledDate);
    final shift = options.shifts
        .where((s) => s.shiftCode == order.shiftCode)
        .firstOrNull;
    final parts = shift?.startLocal.split(':');
    if (date == null || parts == null || parts.length != 2) return null;
    final hour = int.tryParse(parts[0]);
    final minute = int.tryParse(parts[1]);
    if (hour == null || minute == null) return null;
    return DateTime.utc(date.year, date.month, date.day, hour, minute);
  }

  Future<void> _loadQuotes() async {
    try {
      final list = await _addresses.getAddresses();
      if (list.isEmpty) {
        if (mounted) setState(() => _quotes = null);
        return;
      }
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
      setState(() {
        _quotes = _QuickQuotes(address, byTier);
        if (!byTier.containsKey(_tier)) _tier = byTier.keys.first;
      });
    } catch (_) {
      // No prices; the booking card still opens the booking form.
    }
  }

  Future<void> _loadFavorites() async {
    try {
      final list = await _favorites.getFavoriteWorkers();
      if (mounted) setState(() => _favoriteList = list);
    } catch (_) {}
  }

  /// "Đặt ca": the form opens with the chosen segment and the default address already ticked.
  void _openBooking() {
    final q = _quotes;
    Navigator.of(context)
        .pushNamed(
          BookingRoutes.booking,
          arguments: q == null
              ? null
              : BookingScreenArgs(
                  serviceTier: _tier,
                  addressId: q.address.addressId,
                ),
        )
        .then((_) {
          if (mounted) _loadNext();
        });
  }

  /// "Đặt lại" on a favourite helper: a basic-segment booking with that helper ticked.
  void _bookAgain(FavoriteWorker worker) {
    Navigator.of(context).pushNamed(
      BookingRoutes.booking,
      arguments: BookingScreenArgs(
        serviceTier: ServiceTier.economy,
        addressId: _quotes?.address.addressId,
        preferredWorkerId: worker.workerId,
      ),
    );
  }

  void _openAddresses() =>
      Navigator.of(context).pushNamed(CustomerRoutes.addressList).then((_) {
        if (mounted) _loadQuotes();
      });

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
    // The shell's bar and its raised centre button sit over the bottom of this tab: keep the end
    // clear of both (the button rises about 30 px above the bar).
    final bottom = MediaQuery.paddingOf(context).bottom;
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      body: ListView(
        padding: EdgeInsets.only(bottom: 120 + bottom - _overlap),
        children: [
          _header(context),
          // Everything below rises over the header; the list ends [_overlap] earlier to match.
          Transform.translate(
            offset: const Offset(0, -_overlap),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                _bookingCard(),
                if (_favoriteList.isNotEmpty) ..._favoriteSection(),
              ],
            ),
          ),
        ],
      ),
    );
  }

  // ---------------------------------------------------------------- header

  Widget _header(BuildContext context) {
    final top = MediaQuery.paddingOf(context).top;
    return Container(
      clipBehavior: Clip.antiAlias,
      decoration: const BoxDecoration(
        color: NordicColors.contrastDark,
        borderRadius: BorderRadius.vertical(
          bottom: Radius.circular(NordicRadius.sheet + 4),
        ),
      ),
      child: Stack(
        children: [
          const Positioned(
            right: -110,
            top: -90,
            child: IgnorePointer(
              child: ExcludeSemantics(
                child: CustomPaint(size: Size(300, 300), painter: _Rings()),
              ),
            ),
          ),
          Padding(
            padding: EdgeInsets.fromLTRB(
              NordicSpace.gutter,
              top + NordicSpace.lg,
              NordicSpace.gutter,
              _overlap + NordicSpace.section,
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  _today,
                  style: NordicTypography.bodyRegular.copyWith(
                    color: NordicColors.onDarkMuted,
                    fontWeight: FontWeight.w500,
                  ),
                ),
                const SizedBox(height: NordicSpace.xxs),
                Text(
                  _firstName == null ? 'Xin chào' : 'Chào $_firstName',
                  key: const Key('home-greeting'),
                  style: NordicTypography.greeting.copyWith(
                    color: NordicColors.textInverse,
                  ),
                ),
                if (_quotes != null) ...[
                  const SizedBox(height: NordicSpace.md),
                  _addressPill(_quotes!.address),
                ],
                const SizedBox(height: NordicSpace.gutter),
                _nextSection(),
              ],
            ),
          ),
        ],
      ),
    );
  }

  /// A translucent white panel on the dark header.
  static BoxDecoration get _glass => BoxDecoration(
    color: NordicColors.textInverse.withValues(alpha: 0.08),
    borderRadius: NordicRadius.cardAll,
    border: Border.all(color: NordicColors.textInverse.withValues(alpha: 0.14)),
  );

  Widget _addressPill(BookingAddress address) {
    return Pressable(
      child: Semantics(
        button: true,
        label: 'Địa chỉ: ${address.addressLine}. Đổi địa chỉ',
        child: Material(
          color: NordicColors.textInverse.withValues(alpha: 0.08),
          shape: StadiumBorder(
            side: BorderSide(
              color: NordicColors.textInverse.withValues(alpha: 0.14),
            ),
          ),
          clipBehavior: Clip.antiAlias,
          child: InkWell(
            key: const Key('home-address'),
            onTap: _openAddresses,
            child: ConstrainedBox(
              constraints: const BoxConstraints(minHeight: 44),
              child: Padding(
                padding: const EdgeInsets.fromLTRB(
                  NordicSpace.sm,
                  0,
                  NordicSpace.sm,
                  0,
                ),
                child: ExcludeSemantics(
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      const Icon(
                        Icons.location_on_outlined,
                        size: 18,
                        color: NordicColors.onDarkMuted,
                      ),
                      const SizedBox(width: NordicSpace.xs),
                      Flexible(
                        child: Text(
                          '${address.addressLine} · ${formatArea(address.totalAreaM2)}',
                          key: const Key('quick-quote-address'),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: NordicTypography.labelMedium.copyWith(
                            color: NordicColors.textInverse,
                          ),
                        ),
                      ),
                      const SizedBox(width: NordicSpace.xxs),
                      const Icon(
                        Icons.expand_more,
                        size: 18,
                        color: NordicColors.textInverse,
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }

  /// The next booking, on the dark header: a loading block, an error with retry, a one-line
  /// notice when there is none, or the booking itself.
  Widget _nextSection() {
    if (!_nextLoaded) {
      return Container(height: 120, decoration: _glass);
    }
    if (_nextError != null) {
      return NordicCard(
        key: const Key('home-next-error'),
        elevated: false,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              'Lịch ca chưa sẵn sàng',
              style: NordicTypography.titleSmall,
            ),
            const SizedBox(height: NordicSpace.xxs),
            Text(_nextError!, style: NordicTypography.bodySmall),
            const SizedBox(height: NordicSpace.sm),
            NordicButton(
              label: 'Thử lại',
              variant: NordicButtonVariant.secondary,
              onPressed: _loadNext,
            ),
          ],
        ),
      );
    }
    final next = _next;
    if (next == null) {
      // A notice, not a button: booking is the card right below.
      return Container(
        key: const Key('home-no-order'),
        padding: const EdgeInsets.symmetric(
          horizontal: NordicSpace.md,
          vertical: NordicSpace.sm,
        ),
        decoration: _glass,
        child: Row(
          children: [
            const Icon(
              Icons.event_available_outlined,
              size: 20,
              color: NordicColors.onDarkMuted,
            ),
            const SizedBox(width: NordicSpace.sm),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    'Chưa có ca nào sắp tới',
                    style: NordicTypography.titleSmall.copyWith(
                      color: NordicColors.textInverse,
                    ),
                  ),
                  const SizedBox(height: 2),
                  Text(
                    'Ca bạn đặt sẽ hiện ở đây.',
                    style: NordicTypography.bodySmall.copyWith(
                      color: NordicColors.onDarkMuted,
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      );
    }
    return _nextCard(next);
  }

  Widget _nextCard(_NextOrder next) {
    final order = next.order;
    final steps = orderTimeline(order.orderStatus);
    final worker = next.worker;
    return Pressable(
      child: Material(
        color: NordicColors.textInverse.withValues(alpha: 0.08),
        shape: RoundedRectangleBorder(
          borderRadius: NordicRadius.cardAll,
          side: BorderSide(
            color: NordicColors.textInverse.withValues(alpha: 0.14),
          ),
        ),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          key: const Key('home-next-order'),
          onTap: () => _openOrder(order.orderId),
          child: Padding(
            padding: const EdgeInsets.all(NordicSpace.md),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(
                      child: Text(
                        'CA SẮP TỚI',
                        style: NordicTypography.caps.copyWith(
                          color: NordicColors.onDarkMuted,
                        ),
                      ),
                    ),
                    Container(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 10,
                        vertical: NordicSpace.xxs,
                      ),
                      decoration: const BoxDecoration(
                        color: NordicColors.primaryFixed,
                        borderRadius: NordicRadius.pill,
                      ),
                      child: Text(
                        orderStatusLabel(order.orderStatus),
                        style: NordicTypography.bodySmall.copyWith(
                          fontWeight: FontWeight.w600,
                          color: NordicColors.primary,
                        ),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: NordicSpace.sm),
                Row(
                  crossAxisAlignment: CrossAxisAlignment.end,
                  children: [
                    Flexible(
                      child: Text(
                        next.startLocal ?? shiftLabel(order.shiftCode),
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: NordicTypography.numberMedium.copyWith(
                          color: NordicColors.textInverse,
                        ),
                      ),
                    ),
                    if (next.endLocal != null) ...[
                      const SizedBox(width: NordicSpace.xs),
                      Padding(
                        padding: const EdgeInsets.only(bottom: 2),
                        child: Text(
                          'đến ${next.endLocal}',
                          style: NordicTypography.bodyRegular.copyWith(
                            color: NordicColors.onDarkMuted,
                          ),
                        ),
                      ),
                    ],
                  ],
                ),
                const SizedBox(height: NordicSpace.xxs),
                Text(
                  '${scheduleText(order.scheduledDate, order.shiftCode)} · ${tierLabel(order.serviceTier)}',
                  style: NordicTypography.bodyRegular.copyWith(
                    color: NordicColors.onDarkMuted,
                  ),
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
                                      alpha: 0.24,
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
                        size: 40,
                        background: NordicColors.amberSoft,
                        foreground: NordicColors.onAmberSoft,
                      ),
                      const SizedBox(width: NordicSpace.sm),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              workerName(worker),
                              style: NordicTypography.titleSmall.copyWith(
                                color: NordicColors.textInverse,
                              ),
                            ),
                            Text(
                              'Người giúp việc của bạn',
                              style: NordicTypography.bodySmall.copyWith(
                                color: NordicColors.onDarkMuted,
                              ),
                            ),
                          ],
                        ),
                      ),
                      if (ratingText(worker.workerRatingAvg).isNotEmpty) ...[
                        const Icon(
                          Icons.star_rounded,
                          size: 18,
                          color: NordicColors.accentAmber,
                        ),
                        const SizedBox(width: 2),
                        Text(
                          ratingText(worker.workerRatingAvg),
                          style: NordicTypography.labelMedium.copyWith(
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

  // ---------------------------------------------------------- booking card

  /// The raised card over the header: the segments with the server's prices and one button.
  Widget _bookingCard() {
    final q = _quotes;
    final selected = q?.byTier[_tier];
    return Container(
      margin: const EdgeInsets.symmetric(horizontal: NordicSpace.md),
      padding: const EdgeInsets.all(NordicSpace.gutter),
      decoration: const BoxDecoration(
        color: NordicColors.surface,
        borderRadius: BorderRadius.all(Radius.circular(NordicRadius.sheet)),
        boxShadow: NordicElevation.raised,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            _next == null ? 'Hôm nay nhà bạn cần gì?' : 'Đặt thêm ca',
            style: NordicTypography.titleMedium,
          ),
          const SizedBox(height: 2),
          Text(
            q == null
                ? 'Thêm địa chỉ để xem giá theo diện tích nhà bạn.'
                : 'Giá cố định theo diện tích nhà · ngày giờ chọn ở bước sau.',
            style: NordicTypography.bodySmall,
          ),
          const SizedBox(height: NordicSpace.md),
          if (q != null) ...[
            IntrinsicHeight(
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  for (final tier in [ServiceTier.economy, ServiceTier.premium])
                    if (q.byTier[tier] != null) ...[
                      if (tier == ServiceTier.premium &&
                          q.byTier[ServiceTier.economy] != null)
                        const SizedBox(width: NordicSpace.sm),
                      Expanded(child: _tierTile(tier, q.byTier[tier]!)),
                    ],
                ],
              ),
            ),
            const SizedBox(height: NordicSpace.md),
          ] else ...[
            Align(
              alignment: Alignment.centerLeft,
              child: NordicButton(
                key: const Key('home-add-address'),
                label: 'Thêm địa chỉ',
                variant: NordicButtonVariant.secondary,
                icon: const Icon(Icons.add_location_alt_outlined, size: 18),
                onPressed: _openAddresses,
              ),
            ),
            const SizedBox(height: NordicSpace.md),
          ],
          DecoratedBox(
            decoration: const BoxDecoration(
              borderRadius: NordicRadius.pill,
              boxShadow: NordicElevation.cta,
            ),
            child: NordicButton(
              key: const Key('home-book'),
              label: selected == null
                  ? 'Đặt ca'
                  : 'Đặt ca · ${formatVnd(selected.unitPrice)}',
              icon: const Icon(Icons.arrow_forward, size: 20),
              onPressed: _openBooking,
            ),
          ),
        ],
      ),
    );
  }

  /// One segment: a selectable tile (accent border and check mark when chosen).
  Widget _tierTile(String tier, PriceQuote quote) {
    final on = _tier == tier;
    final options = _options;
    return Pressable(
      child: Semantics(
        selected: on,
        button: true,
        child: Material(
          color: on ? NordicColors.surfaceSubtle : NordicColors.surface,
          shape: RoundedRectangleBorder(
            borderRadius: NordicRadius.cardAll,
            side: BorderSide(
              color: on ? NordicColors.primaryContainer : NordicColors.border,
              width: on ? 2 : 1,
            ),
          ),
          clipBehavior: Clip.antiAlias,
          child: InkWell(
            key: Key('home-quote-$tier'),
            onTap: () => setState(() => _tier = tier),
            child: Padding(
              padding: EdgeInsets.all(on ? 13 : 14),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: Text(
                          tierLabel(tier),
                          style: NordicTypography.titleSmall,
                        ),
                      ),
                      AnimatedOpacity(
                        opacity: on ? 1 : 0,
                        duration: const Duration(milliseconds: 150),
                        child: Container(
                          width: 22,
                          height: 22,
                          decoration: const BoxDecoration(
                            color: NordicColors.primaryContainer,
                            shape: BoxShape.circle,
                          ),
                          child: const Icon(
                            Icons.check,
                            size: 14,
                            color: NordicColors.textInverse,
                          ),
                        ),
                      ),
                    ],
                  ),
                  if (options != null) ...[
                    const SizedBox(height: NordicSpace.xxs),
                    Expanded(
                      child: Text(
                        tierDescription(tier, options),
                        style: NordicTypography.bodySmall,
                      ),
                    ),
                  ] else
                    const Spacer(),
                  const SizedBox(height: NordicSpace.sm),
                  Text(
                    formatVnd(quote.unitPrice),
                    style: NordicTypography.numberSmall,
                  ),
                  const SizedBox(height: NordicSpace.xxs),
                  Text(
                    'mỗi người, mỗi ca · cần ${quote.requiredWorkers} người',
                    style: NordicTypography.bodySmall,
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  // ------------------------------------------------------------ favourites

  static const _tints = [
    (NordicColors.amberSoft, NordicColors.onAmberSoft),
    (NordicColors.primaryFixed, NordicColors.primary),
    (NordicColors.surfaceContainer, NordicColors.textTitle),
  ];

  List<Widget> _favoriteSection() => [
    Padding(
      padding: const EdgeInsets.fromLTRB(
        NordicSpace.gutter,
        NordicSpace.xxs,
        NordicSpace.gutter,
        0,
      ),
      child: SectionTitle(
        'Người giúp việc yêu thích',
        trailing: 'Tất cả',
        onAction: widget.onShowFavorites,
      ),
    ),
    SizedBox(
      // Grows with the text scale so the card keeps room for its rows.
      height: 244 + (MediaQuery.textScalerOf(context).scale(1) - 1) * 80,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.fromLTRB(
          NordicSpace.gutter,
          NordicSpace.xxs,
          NordicSpace.gutter,
          NordicSpace.md,
        ),
        itemCount: _favoriteList.length,
        separatorBuilder: (_, _) => const SizedBox(width: NordicSpace.sm),
        itemBuilder: (_, i) => _favoriteCard(_favoriteList[i], i),
      ),
    ),
  ];

  Widget _favoriteCard(FavoriteWorker w, int index) {
    final (tint, ink) = _tints[index % _tints.length];
    final rating = ratingText(w.ratingAvg);
    return SizedBox(
      width: 164,
      child: NordicCard(
        key: Key('home-favorite-${w.workerId}'),
        padding: const EdgeInsets.all(NordicSpace.md - 2),
        onTap: widget.onShowFavorites,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            AvatarInitials(
              w.fullName,
              size: 48,
              background: tint,
              foreground: ink,
            ),
            const SizedBox(height: NordicSpace.sm),
            Text(
              w.fullName,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: NordicTypography.titleSmall,
            ),
            const SizedBox(height: 2),
            Row(
              children: [
                if (rating.isNotEmpty) ...[
                  const Icon(
                    Icons.star_rounded,
                    size: 15,
                    color: NordicColors.accentAmber,
                  ),
                  const SizedBox(width: 2),
                ],
                Flexible(
                  child: Text(
                    rating.isEmpty
                        ? '${w.completedJobs} ca'
                        : '$rating · ${w.completedJobs} ca',
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: NordicTypography.bodySmall,
                  ),
                ),
              ],
            ),
            const SizedBox(height: NordicSpace.xxs),
            Text(
              w.statusDisplayName,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: NordicTypography.bodySmall.copyWith(
                color: NordicColors.primaryContainer,
                fontWeight: FontWeight.w600,
              ),
            ),
            const Spacer(),
            SizedBox(
              width: double.infinity,
              child: NordicButton(
                key: Key('home-book-again-${w.workerId}'),
                label: 'Đặt lại',
                variant: NordicButtonVariant.secondary,
                onPressed: () => _bookAgain(w),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// Three thin rings in the corner of the header: depth without a gradient.
class _Rings extends CustomPainter {
  const _Rings();

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1.5
      ..color = NordicColors.textInverse.withValues(alpha: 0.07);
    final center = size.center(Offset.zero);
    for (final r in const [60.0, 100.0, 140.0]) {
      canvas.drawCircle(center, r, paint);
    }
  }

  @override
  bool shouldRepaint(covariant CustomPainter oldDelegate) => false;
}
