import '../../workers/logic/acceptance_logic.dart';

import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/avatar_initials.dart';
import '../../../core/widgets/bottom_action_bar.dart';
import '../../../core/widgets/dark_header.dart';
import '../../../core/widgets/grouped_list_card.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/nordic_text_input.dart';
import '../../../core/widgets/raised_panel.dart';
import '../../../core/widgets/section_title.dart';
import '../../../core/widgets/status_pill.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../../customers/models/customer_address.dart';
import '../../customers/services/customer_address_service.dart';
import '../../customers/services/favorite_worker_service.dart';
import '../../disputes/models/dispute_models.dart';
import '../../disputes/routes.dart';
import '../../disputes/screens/dispute_screen.dart';
import '../../payments/models/payment_models.dart';
import '../../payments/routes.dart';
import '../../ratings/models/rating_models.dart';
import '../../ratings/routes.dart';
import '../../ratings/screens/rating_screen.dart';
import '../../ratings/services/ratings_service.dart';
import '../../workers/routes.dart';
import '../../workers/screens/customer_acceptance_screen.dart';
import '../logic/booking_logic.dart';
import '../logic/order_logic.dart';
import '../models/booking_models.dart';
import '../routes.dart';
import 'booking_screen.dart';
import '../services/fallback_service.dart';
import '../services/worker_route_service.dart';
import '../services/order_service.dart';
import 'arrival_prompt_card.dart';
import 'fallback_prompt_card.dart';
import 'order_status_pill.dart';

/// One order of the customer: its timeline and workers (MOB-M2-04), cancel, and "Làm lần 2" (MOB-M2-05).
/// Contract booking.md 3.5 to 3.8.
///
/// There is no realtime client in the project, so the screen asks the server again every few seconds (the contract's fallback)
/// while the order can still change.
class OrderDetailScreen extends StatefulWidget {
  /// The order to show; when null it is read from the route arguments.
  final OrderDetailArgs? args;
  final IOrderService? service;

  /// The BR-02 fallback question (an order with one worker and no second one); by default the real server.
  final IFallbackService? fallbackService;

  /// Retained for callers using the previous constructor.
  final FavoriteWorkerService? favorites;
  final IRatingsService? ratings;

  /// How often the order is asked again.
  final Duration pollInterval;

  /// What "Thanh toán" does; by default it opens the payment screen and reloads the order when the customer comes back.
  final void Function(PaymentScreenArgs args)? onPay;

  /// What the acceptance, rating and dispute buttons do (the route name and its arguments); by default it opens that screen
  /// and reloads the order when the customer comes back.
  final void Function(String route, Object args)? onOpen;

  /// The address of the order, for the "Địa điểm" card; by default read from the customer's address book.
  final Future<CustomerAddress?> Function(int addressId)? addressLookup;

  /// The worker's way to the house for the "đang trên đường tới" card (MOB-ORDER-03).
  final IWorkerRouteService? routeService;

  const OrderDetailScreen({
    super.key,
    this.args,
    this.service,
    this.fallbackService,
    this.favorites,
    this.ratings,
    this.pollInterval = defaultProgressPollInterval,
    this.onPay,
    this.onOpen,
    this.addressLookup,
    this.routeService,
  });

  @override
  State<OrderDetailScreen> createState() => _OrderDetailScreenState();
}

class _OrderDetailScreenState extends State<OrderDetailScreen> {
  late final IOrderService _service;
  late final IFallbackService _fallbackService;
  late final IRatingsService _ratingsService;
  Map<int, bool> _ratingEligibility = {};
  bool _ratingCheckFailed = false;
  int _ratingRequest = 0;
  int? _orderId;
  bool _started = false;

  BookingOrder? _order;

  /// The order's address (MOB-ORDER-02); null until read, or when it is no longer in the address book.
  CustomerAddress? _address;
  int? _addressAsked;
  OrderProgress? _progress;
  String? _error;
  bool _loading = false;
  bool _refreshing = false; // one refresh at a time
  Timer? _pollTimer;

  // Cancel
  final _reason = TextEditingController();
  bool _cancelOpen = false;
  bool _cancelling = false; // one request per tap
  String? _cancelError;
  String? _notice;

  // "Làm lần 2"
  bool _extOpen = false;
  bool _extLoading = false;
  bool _extSubmitting = false; // one request per tap
  BookingOptions? _options;
  int? _extAssignmentId;
  double? _extHours;
  String? _extError;

  // Worker check-in arrival confirmation (CUST-ARRIVAL-01)
  int _arrivalRequest = 0;
  Map<int, CustomerArrivalStatus> _pendingArrivals = {};

  // MOB-ORDER-03: the "on the way" cards (assignment id -> card) and the customer's favourite workers.
  late final IWorkerRouteService _routeService =
      widget.routeService ?? WorkerRouteService();
  late final FavoriteWorkerService _favoriteService =
      widget.favorites ?? FavoriteWorkerService();
  Map<int, WorkerRouteView> _routes = {};
  int _routeRequest = 0;
  Set<int>? _favoriteIds;
  final Set<int> _favoriteBusy = {};
  String? _favoriteError;

  @override
  void initState() {
    super.initState();
    _service = widget.service ?? OrderService();
    _fallbackService = widget.fallbackService ?? FallbackService();
    _ratingsService = widget.ratings ?? RatingsService();
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_started) return;
    _started = true;
    final routeArgs = ModalRoute.of(context)?.settings.arguments;
    _orderId =
        widget.args?.orderId ??
        (routeArgs is OrderDetailArgs ? routeArgs.orderId : null);
    if (_orderId != null) _load();
  }

  @override
  void dispose() {
    _pollTimer?.cancel();
    _reason.dispose();
    super.dispose();
  }

  Future<(BookingOrder, OrderProgress)> _fetch(int orderId) async {
    final both = await Future.wait<Object>([
      _service.getOrder(orderId),
      _service.getProgress(orderId),
    ]);
    return (both[0] as BookingOrder, both[1] as OrderProgress);
  }

  Future<void> _load() async {
    final orderId = _orderId;
    if (orderId == null) return;
    _pollTimer?.cancel();
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final (order, progress) = await _fetch(orderId);
      if (!mounted) return;
      setState(() {
        _order = order;
        _progress = progress;
        _loading = false;
      });
      unawaited(_loadRatingEligibility(order, progress));
      unawaited(_loadArrivals(order, progress));
      unawaited(_loadAddress(order.addressId));
      unawaited(_loadRoutes(order, progress));
      unawaited(_loadFavorites());
      _watch();
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _error = orderLoadFailure(e);
        _loading = false;
      });
    }
  }

  /// Reads the order's address once per address id; any failure just leaves the card out.
  Future<void> _loadAddress(int addressId) async {
    if (_addressAsked == addressId) return;
    _addressAsked = addressId;
    try {
      final lookup = widget.addressLookup ?? _addressFromBook;
      final address = await lookup(addressId);
      if (mounted) setState(() => _address = address);
    } catch (_) {}
  }

  static Future<CustomerAddress?> _addressFromBook(int addressId) async {
    final list = await CustomerAddressService().getAddresses();
    for (final a in list) {
      if (a.addressId == addressId) return a;
    }
    return null;
  }

  Future<void> _loadRatingEligibility(
    BookingOrder order,
    OrderProgress progress,
  ) async {
    final request = ++_ratingRequest;
    final completed = progress.assignments.where(
      (a) => a.assignmentStatus == AssignmentStatus.completed,
    );
    if (completed.isEmpty || order.orderStatus == OrderStatus.cancelled) {
      if (mounted) {
        setState(() {
          _ratingEligibility = {};
          _ratingCheckFailed = false;
        });
      }
      return;
    }
    var failed = false;
    final results = await Future.wait(
      completed.map((assignment) async {
        try {
          final window = await _ratingsService.getWindow(
            RatingRole.customer,
            assignment.assignmentId,
          );
          return (assignment.assignmentId, window.canRate);
        } catch (_) {
          failed = true;
          return (assignment.assignmentId, false);
        }
      }),
    );
    if (!mounted || request != _ratingRequest) return;
    setState(() {
      _ratingCheckFailed = failed;
      _ratingEligibility = Map.fromEntries(
        results.map((result) => MapEntry(result.$1, result.$2)),
      );
    });
  }

  /// The way of each worker still on the road (an ASSIGNED order, assignment ASSIGNED): the Web's `routeCandidates`.
  Future<void> _loadRoutes(BookingOrder order, OrderProgress progress) async {
    final request = ++_routeRequest;
    final onTheWay = order.orderStatus == OrderStatus.assigned
        ? [
            for (final a in progress.assignments)
              if (a.assignmentStatus == AssignmentStatus.assigned)
                a.assignmentId,
          ]
        : const <int>[];
    final results = await Future.wait(
      onTheWay.map((id) async {
        try {
          return (id, await _routeService.getRoute(id));
        } catch (_) {
          return (id, const WorkerRouteView.message(routeUnavailableText));
        }
      }),
    );
    if (!mounted || request != _routeRequest) return;
    setState(() {
      _routes = {for (final (id, view) in results) id: ?view};
    });
  }

  /// Which workers are already favourites, for the hearts; a failure leaves the hearts out.
  Future<void> _loadFavorites() async {
    try {
      final list = await _favoriteService.getFavoriteWorkers();
      if (mounted) {
        setState(() => _favoriteIds = {for (final w in list) w.workerId});
      }
    } catch (_) {}
  }

  /// Adds or removes the worker at once; a failure puts the heart back and says so.
  Future<void> _toggleFavorite(int workerId) async {
    final ids = _favoriteIds;
    if (ids == null || _favoriteBusy.contains(workerId)) return;
    final wasFavorite = ids.contains(workerId);
    setState(() {
      _favoriteBusy.add(workerId);
      _favoriteError = null;
      wasFavorite ? ids.remove(workerId) : ids.add(workerId);
    });
    try {
      if (wasFavorite) {
        await _favoriteService.removeFavoriteWorker(workerId);
      } else {
        await _favoriteService.addFavoriteWorker(workerId);
      }
    } catch (_) {
      if (!mounted) return;
      setState(() {
        wasFavorite ? ids.add(workerId) : ids.remove(workerId);
        _favoriteError = wasFavorite
            ? 'Chưa bỏ yêu thích được. Thử lại nhé.'
            : 'Chưa thêm vào yêu thích được. Thử lại nhé.';
      });
    } finally {
      if (mounted) setState(() => _favoriteBusy.remove(workerId));
    }
  }

  Widget _routeCard(ProgressAssignment a, WorkerRouteView view) {
    final route = view.route;
    return NordicCard(
      key: Key('route-${a.assignmentId}'),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Container(
            width: 40,
            height: 40,
            decoration: const BoxDecoration(
              color: NordicColors.primaryContainer,
              shape: BoxShape.circle,
            ),
            child: const Icon(
              Icons.two_wheeler_rounded,
              size: 22,
              color: NordicColors.primary,
            ),
          ),
          const SizedBox(width: NordicSpace.sm),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  '${workerName(a)} đang trên đường tới',
                  style: NordicTypography.titleSmall,
                ),
                const SizedBox(height: NordicSpace.xxs),
                Text(
                  route == null
                      ? view.message!
                      : 'Cách ${formatRouteDistance(route.distanceMeters)} · ${formatRouteDuration(route.durationSeconds)}',
                  key: Key('route-text-${a.assignmentId}'),
                  style: NordicTypography.bodySmall,
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _favoriteHeart(ProgressAssignment a) {
    final favorite = _favoriteIds!.contains(a.workerId);
    return IconButton(
      key: Key('favorite-${a.assignmentId}'),
      tooltip: favorite ? 'Bỏ yêu thích' : 'Thêm vào yêu thích',
      constraints: const BoxConstraints(minWidth: 44, minHeight: 44),
      onPressed: _favoriteBusy.contains(a.workerId)
          ? null
          : () => _toggleFavorite(a.workerId),
      icon: Icon(
        favorite ? Icons.favorite_rounded : Icons.favorite_border_rounded,
        color: favorite ? NordicColors.error : NordicColors.textSecondary,
      ),
    );
  }

  Future<void> _loadArrivals(BookingOrder order, OrderProgress progress) async {
    final request = ++_arrivalRequest;
    if (order.orderStatus != OrderStatus.assigned) {
      if (mounted && _pendingArrivals.isNotEmpty) {
        setState(() => _pendingArrivals = {});
      }
      return;
    }
    final assigned = progress.assignments.where(
      (a) => a.assignmentStatus == AssignmentStatus.assigned,
    );
    if (assigned.isEmpty) {
      if (mounted && _pendingArrivals.isNotEmpty) {
        setState(() => _pendingArrivals = {});
      }
      return;
    }
    final results = await Future.wait(
      assigned.map((a) async {
        try {
          final arrival = await _service.getArrivalStatus(a.assignmentId);
          return (a.assignmentId, arrival);
        } catch (_) {
          return (a.assignmentId, null);
        }
      }),
    );
    if (!mounted || request != _arrivalRequest) return;
    final map = <int, CustomerArrivalStatus>{};
    for (final (id, arrival) in results) {
      if (arrival != null && arrival.awaitingCustomerConfirmation) {
        map[id] = arrival;
      }
    }
    setState(() {
      _pendingArrivals = map;
    });
  }

  /// Keeps asking while the order can still change.
  void _watch() {
    _pollTimer?.cancel();
    _pollTimer = null;
    final order = _order;
    if (order == null || !shouldPollOrder(order.orderStatus)) return;
    _pollTimer = Timer.periodic(widget.pollInterval, (_) => _refresh());
  }

  /// A quiet reload: a failure keeps what is on screen and the next tick tries again.
  Future<void> _refresh() async {
    final orderId = _orderId;
    if (orderId == null || _refreshing || _cancelling || _extSubmitting) return;
    _refreshing = true;
    try {
      final (order, progress) = await _fetch(orderId);
      if (!mounted) return;
      setState(() {
        _order = order;
        _progress = progress;
      });
      unawaited(_loadRatingEligibility(order, progress));
      unawaited(_loadArrivals(order, progress));
      unawaited(_loadRoutes(order, progress));
      if (!shouldPollOrder(order.orderStatus)) {
        _pollTimer?.cancel();
        _pollTimer = null;
      }
    } on ApiException {
      // Kept quiet on purpose, see above.
    } finally {
      _refreshing = false;
    }
  }

  void _pay({int? extensionId}) {
    final order = _order;
    if (order == null) return;
    final args = PaymentScreenArgs(
      orderId: order.orderId,
      extensionId: extensionId,
      orderCode: order.orderCode,
    );
    if (widget.onPay != null) return widget.onPay!(args);
    Navigator.of(context)
        .pushNamed(PaymentRoutes.payment, arguments: args)
        .then((_) {
          if (mounted) _refresh();
        });
  }

  void _open(String route, Object args) {
    if (widget.onOpen != null) return widget.onOpen!(route, args);
    Navigator.of(context).pushNamed(route, arguments: args).then((_) {
      if (mounted) _refresh();
    });
  }

  Future<void> _cancel() async {
    final order = _order;
    if (order == null || _cancelling) return;
    final problem = validateCancelReason(_reason.text);
    if (problem != null) return setState(() => _cancelError = problem);

    _cancelling = true;
    setState(() => _cancelError = null);
    try {
      final cancelled = await _service.cancelOrder(
        order.orderId,
        _reason.text.trim(),
      );
      if (!mounted) return;
      setState(() {
        _order = cancelled;
        _cancelOpen = false;
        _extOpen = false;
        _notice = 'Đã huỷ đơn.';
      });
      _watch();
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() => _cancelError = cancelFailure(e));
    } finally {
      _cancelling = false;
      if (mounted) setState(() {});
    }
  }

  Future<void> _openExtension() async {
    setState(() {
      _extOpen = true;
      _extError = null;
    });
    if (_options != null || _extLoading) return;
    setState(() => _extLoading = true);
    try {
      final options = await _service.getOptions();
      if (!mounted) return;
      setState(() {
        _options = options;
        _extLoading = false;
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _extError = orderLoadFailure(e);
        _extLoading = false;
      });
    }
  }

  Future<void> _requestExtension(List<ProgressAssignment> workers) async {
    final order = _order;
    final progress = _progress;
    if (order == null ||
        progress == null ||
        _extSubmitting ||
        workers.isEmpty) {
      return;
    }
    final hours = _extHours;
    if (hours == null) {
      return setState(() => _extError = 'Chọn số giờ làm thêm.');
    }
    final assignmentId = workers.any((a) => a.assignmentId == _extAssignmentId)
        ? _extAssignmentId!
        : workers.first.assignmentId;

    _extSubmitting = true;
    setState(() => _extError = null);
    try {
      final extension = await _service.requestExtension(
        orderId: order.orderId,
        assignmentId: assignmentId,
        extraHours: hours,
      );
      if (!mounted) return;
      setState(() {
        _progress = OrderProgress(
          orderId: progress.orderId,
          orderStatus: progress.orderStatus,
          requiredWorkers: progress.requiredWorkers,
          assignments: progress.assignments,
          extension: extension,
          substituteSearchUntil: progress.substituteSearchUntil,
        );
        _extOpen = false;
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() => _extError = extensionFailure(e));
    } finally {
      _extSubmitting = false;
      if (mounted) setState(() {});
    }
  }

  /// How far the raised panel rises over the dark header.
  static const _overlap = 64.0;

  @override
  Widget build(BuildContext context) {
    final order = _order;
    final progress = _progress;
    if (_orderId != null &&
        _error == null &&
        order != null &&
        progress != null) {
      return _ready(order, progress);
    }
    // A title in words; the order code sits small in the facts (MOB-UI-15, DESIGN-SYSTEM §6).
    // Paying is the one main action of an unpaid order: it sits in the bar.
    return NordicPage.pushed(
      title: 'Chi tiết đơn',
      titleKey: const Key('order-title'),
      onRefresh: _orderId == null ? null : _refresh,
      actions: order != null && canPay(order.orderStatus)
          ? [
              NordicButton(
                key: const Key('pay-order'),
                label: 'Thanh toán ${formatVnd(order.totalAmount)}',
                onPressed: _pay,
              ),
            ]
          : null,
      children: _body(),
    );
  }

  List<Widget> _body() {
    if (_orderId == null) {
      return const [
        NordicCard(
          child: Text(
            'Hãy mở một đơn trong tab Đơn.',
            style: NordicTypography.bodyRegular,
          ),
        ),
      ];
    }
    if (_loading && _order == null) {
      return const [LoadingStateWidget(message: 'Đang tải đơn…')];
    }
    if (_error != null) {
      return [
        ErrorStateWidget(
          title: 'Chưa tải được đơn',
          message: _error!,
          onRetry: _load,
        ),
      ];
    }

    final order = _order;
    final progress = _progress;
    if (order == null || progress == null) return const [];

    return const [];
  }

  /// `08:00` in Asia/Ho_Chi_Minh from the server's shift start, or null when the server sent none.
  static String? _localTime(DateTime? instant) {
    if (instant == null) return null;
    final t = instant.toUtc().add(const Duration(hours: 7));
    return '${t.hour.toString().padLeft(2, '0')}:${t.minute.toString().padLeft(2, '0')}';
  }

  static const _paidStatuses = {
    OrderStatus.paid,
    OrderStatus.dispatching,
    OrderStatus.awaitingFallback,
    OrderStatus.assigned,
    OrderStatus.completed,
  };

  /// The screen of an order that is loaded (customer redesign v2 §4.2): a dark header with the big time, the facts on a
  /// raised panel over it, then the timeline, the workers, the other things to do; the main button in the bar.
  Widget _ready(BookingOrder order, OrderProgress progress) {
    final displayStatus = orderStatusForDisplay(
      order.orderStatus,
      progress.assignments,
    );
    final bar = _bar(order, progress);
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      body: Column(
        children: [
          Expanded(
            child: RefreshIndicator(
              onRefresh: _refresh,
              child: ListView(
                padding: EdgeInsets.zero,
                children: [
                  DarkHeader(
                    overlap: _overlap,
                    appBarTitle: 'Chi tiết đơn',
                    titleKey: const Key('order-title'),
                    child: _headerContent(order, displayStatus),
                  ),
                  Transform.translate(
                    offset: const Offset(0, -_overlap),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        RaisedPanel(child: _facts(order, displayStatus)),
                        Padding(
                          padding: const EdgeInsets.fromLTRB(
                            NordicSpace.gutter,
                            NordicSpace.sm,
                            NordicSpace.gutter,
                            NordicSpace.lg,
                          ),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.stretch,
                            children: _sections(order, progress, displayStatus),
                          ),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          ),
          ?bar,
        ],
      ),
    );
  }

  /// In the dark header: the status, the start time as the big figure, the day and the shift.
  Widget _headerContent(BookingOrder order, String displayStatus) {
    final start = _localTime(order.shiftStartAt);
    final end = _localTime(order.shiftEndAt);
    final cancelled = order.orderStatus == OrderStatus.cancelled;
    final muted = NordicTypography.bodyRegular.copyWith(
      color: NordicColors.onDarkMuted,
    );
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        OrderStatusPill(displayStatus, textKey: const Key('order-status')),
        const SizedBox(height: NordicSpace.sm),
        Row(
          crossAxisAlignment: CrossAxisAlignment.end,
          children: [
            Flexible(
              child: Text(
                start ?? shiftLabel(order.shiftCode),
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: NordicTypography.numberLarge.copyWith(
                  fontSize: 44,
                  color: cancelled
                      ? NordicColors.onDarkMuted
                      : NordicColors.textInverse,
                ),
              ),
            ),
            if (end != null) ...[
              const SizedBox(width: NordicSpace.xs),
              Padding(
                padding: const EdgeInsets.only(bottom: 6),
                child: Text('đến $end', style: muted),
              ),
            ],
          ],
        ),
        const SizedBox(height: NordicSpace.xxs),
        Text(
          scheduleText(order.scheduledDate, order.shiftCode),
          key: const Key('order-schedule'),
          style: muted,
        ),
      ],
    );
  }

  /// On the raised panel: why it was cancelled, the amount, package, area, workers and the order code.
  Widget _facts(BookingOrder order, String displayStatus) {
    final cancelled = order.orderStatus == OrderStatus.cancelled;
    final amount = Text(
      formatVnd(order.totalAmount),
      key: const Key('order-amount'),
      style: NordicTypography.numberMedium,
    );
    Widget cell(String label, Widget value) => Expanded(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: NordicTypography.bodySmall),
          const SizedBox(height: NordicSpace.xxs),
          value,
        ],
      ),
    );
    Widget text(String v) => Text(v, style: NordicTypography.titleSmall);
    final reasonText = cancelled
        ? ((order.cancelReason ?? '').isNotEmpty
              ? cancelReasonView(order.cancelReason!).text
              : 'Đơn này đã huỷ.')
        : null;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (reasonText != null) ...[
          Container(
            width: double.infinity,
            padding: const EdgeInsets.all(NordicSpace.sm),
            decoration: const BoxDecoration(
              color: NordicColors.amberSoft,
              borderRadius: NordicRadius.fieldAll,
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'Đơn đã huỷ',
                  style: NordicTypography.titleSmall.copyWith(
                    color: NordicColors.onAmberSoft,
                  ),
                ),
                const SizedBox(height: NordicSpace.xxs),
                Text(
                  reasonText,
                  key: const Key('order-cancel-reason'),
                  style: NordicTypography.bodyRegular.copyWith(
                    color: NordicColors.onAmberSoft,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: NordicSpace.md),
        ],
        if (!cancelled) ...[
          Row(
            crossAxisAlignment: CrossAxisAlignment.center,
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('Tổng tiền', style: NordicTypography.bodyRegular),
                    if (_paidStatuses.contains(order.orderStatus)) ...[
                      const SizedBox(height: NordicSpace.xxs),
                      const StatusPill(
                        'Đã thanh toán',
                        key: Key('order-paid-pill'),
                        tone: PillTone.ok,
                      ),
                    ],
                  ],
                ),
              ),
              const SizedBox(width: NordicSpace.xs),
              Flexible(
                child: FittedBox(
                  fit: BoxFit.scaleDown,
                  alignment: Alignment.centerRight,
                  child: amount,
                ),
              ),
            ],
          ),
          const SizedBox(height: NordicSpace.md),
        ],
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            cell('Gói', text(tierLabel(order.serviceTier))),
            cell('Diện tích', text(formatArea(order.areaSnapshotM2))),
            cell(
              cancelled ? 'Số tiền' : 'Người làm',
              cancelled
                  ? FittedBox(
                      fit: BoxFit.scaleDown,
                      alignment: Alignment.centerLeft,
                      child: amount,
                    )
                  : text('${order.requiredWorkers} người'),
            ),
          ],
        ),
        if ((order.customerNote ?? '').isNotEmpty) ...[
          const SizedBox(height: NordicSpace.sm),
          Text(
            'Ghi chú: ${order.customerNote}',
            style: NordicTypography.bodySmall,
          ),
        ],
        const SizedBox(height: NordicSpace.md),
        Container(
          padding: const EdgeInsets.only(left: NordicSpace.sm),
          decoration: const BoxDecoration(
            color: NordicColors.surfaceSubtle,
            borderRadius: NordicRadius.fieldAll,
          ),
          child: Row(
            children: [
              Expanded(
                child: Text(
                  'Mã đơn ${order.orderCode}',
                  key: const Key('order-code'),
                  style: NordicTypography.labelMedium,
                ),
              ),
              Semantics(
                button: true,
                label: 'Sao chép mã đơn',
                child: InkWell(
                  key: const Key('order-code-copy'),
                  onTap: () => _copyCode(order.orderCode),
                  child: const SizedBox(
                    width: 44,
                    height: 44,
                    child: ExcludeSemantics(
                      child: Icon(
                        Icons.copy_rounded,
                        size: 18,
                        color: NordicColors.primary,
                      ),
                    ),
                  ),
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }

  Future<void> _copyCode(String code) async {
    await Clipboard.setData(ClipboardData(text: code));
    if (!mounted) return;
    ScaffoldMessenger.of(context)
        .showSnackBar(const SnackBar(content: Text('Đã sao chép mã đơn')));
  }

  /// Below the raised panel: what the customer reads and does, in the order of the spec.
  List<Widget> _sections(
    BookingOrder order,
    OrderProgress progress,
    String displayStatus,
  ) {
    final workers = extendableAssignments(progress);
    final extension = progress.extension;
    final actions = _actions(order, progress);
    final cancelled = order.orderStatus == OrderStatus.cancelled;
    return [
      if (canPay(order.orderStatus)) ...[
        const NordicCard(
          child: Text(
            'Thanh toán để hệ thống bắt đầu tìm người giúp việc cho bạn.',
            style: NordicTypography.bodyRegular,
          ),
        ),
        const SizedBox(height: NordicSpace.sm),
      ],
      if (_notice != null) ...[
        Text(
          _notice!,
          key: const Key('order-notice'),
          style: NordicTypography.bodyRegular,
        ),
        const SizedBox(height: NordicSpace.sm),
      ],
      if (_ratingCheckFailed)
        ErrorStateWidget(
          title: 'Chưa kiểm tra được đánh giá',
          message: 'Thử lại để xem bạn có thể đánh giá người làm không.',
          onRetry: () => _loadRatingEligibility(order, progress),
        ),
      if (order.orderStatus == OrderStatus.awaitingFallback) ...[
        FallbackPromptCard(
          key: ValueKey('fallback-${order.orderId}'),
          orderId: order.orderId,
          service: _fallbackService,
          onAnswered: _refresh,
        ),
        const SizedBox(height: NordicSpace.sm),
      ],
      if (order.orderStatus == OrderStatus.dispatching &&
          progress.substituteSearchUntil != null) ...[
        _substituteSearchCard(progress.substituteSearchUntil!),
        const SizedBox(height: NordicSpace.sm),
      ],
      if (_pendingArrivals.isNotEmpty) ...[
        for (final entry in _pendingArrivals.entries) ...[
          Builder(
            builder: (context) {
              final assignment = progress.assignments
                  .cast<ProgressAssignment?>()
                  .firstWhere(
                    (a) => a?.assignmentId == entry.key,
                    orElse: () => null,
                  );
              return ArrivalPromptCard(
                key: ValueKey('arrival-${entry.key}'),
                assignmentId: entry.key,
                workerName: assignment?.workerName,
                distanceMeters: entry.value.distanceMeters,
                service: _service,
                onConfirmed: () {
                  setState(() {
                    _notice = 'Đã xác nhận thợ đã đến.';
                    _pendingArrivals.remove(entry.key);
                  });
                  _refresh();
                },
              );
            },
          ),
          const SizedBox(height: NordicSpace.sm),
        ],
      ],
      for (final a in progress.assignments)
        if (_routes[a.assignmentId] case final view?) ...[
          _routeCard(a, view),
          const SizedBox(height: NordicSpace.sm),
        ],
      const SectionTitle('Tiến trình'),
      NordicCard(child: _timeline(displayStatus, progress.assignments)),
      if (continuationText(progress.continuation) case final note?) ...[
        const SizedBox(height: NordicSpace.sm),
        NordicCard(
          key: const Key('continuation-card'),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Icon(Icons.more_time_outlined, color: NordicColors.primary),
              const SizedBox(width: NordicSpace.xs),
              Expanded(
                child: Text(
                  note,
                  key: const Key('continuation-note'),
                  style: NordicTypography.bodyRegular,
                ),
              ),
            ],
          ),
        ),
      ],
      if (!(cancelled && progress.assignments.isEmpty)) ...[
        SectionTitle(
          'Người làm',
          trailing:
              '${progress.assignments.length}/${progress.requiredWorkers}',
        ),
        if (_favoriteError != null)
          Padding(
            padding: const EdgeInsets.only(bottom: NordicSpace.xs),
            child: Text(
              _favoriteError!,
              key: const Key('favorite-error'),
              style: NordicTypography.bodySmall.copyWith(
                color: NordicColors.error,
              ),
            ),
          ),
        ..._assignments(order, progress),
      ],
      if (extension != null) ...[
        const SizedBox(height: NordicSpace.md),
        _extensionCard(extension),
      ],
      if (_address != null) ...[
        const SectionTitle('Địa điểm'),
        _addressCard(_address!),
      ],
      if (actions.isNotEmpty) ...[
        const SectionTitle('Việc khác'),
        GroupedListCard(items: actions),
      ],
      if (_extOpen &&
          canExtend(progress) &&
          order.orderStatus == OrderStatus.assigned) ...[
        const SizedBox(height: NordicSpace.md),
        _extensionForm(workers),
      ],
      if (canCancel(order.orderStatus, progress.assignments)) ...[
        const SizedBox(height: NordicSpace.lg),
        _cancelSection(),
      ],
    ];
  }

  void _rebook(BookingOrder order, ProgressAssignment a) {
    _open(
      BookingRoutes.booking,
      BookingScreenArgs(
        serviceTier: order.serviceTier,
        addressId: order.addressId,
        preferredWorkerId: a.workerId,
        previousOrderId: order.orderId,
        previousWorkerName: workerName(a),
      ),
    );
  }

  /// The one main button of the state, in the bottom bar; null when the screen has none.
  Widget? _bar(BookingOrder order, OrderProgress progress) {
    if (canPay(order.orderStatus)) {
      return BottomActionBar(
        children: [
          NordicButton(
            key: const Key('pay-order'),
            label: 'Thanh toán ${formatVnd(order.totalAmount)}',
            onPressed: _pay,
          ),
        ],
      );
    }
    if (order.orderStatus == OrderStatus.cancelled) {
      return BottomActionBar(
        children: [
          Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              NordicButton(
                key: const Key('order-rebook'),
                label: 'Chọn lịch khác',
                onPressed: () =>
                    Navigator.of(context).pushNamed(BookingRoutes.booking),
              ),
              NordicButton(
                key: const Key('order-back-to-list'),
                label: 'Về danh sách đơn',
                variant: NordicButtonVariant.ghost,
                onPressed: () => Navigator.of(context).maybePop(),
              ),
            ],
          ),
        ],
      );
    }
    if (order.orderStatus == OrderStatus.completed) {
      final eligible = progress.assignments.where(
        (a) => _ratingEligibility[a.assignmentId] == true,
      );
      if (eligible.isNotEmpty) {
        final assignment = eligible.first;
        return BottomActionBar(
          children: [
            Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const Text(
                  'Bạn có 48 giờ sau khi xong ca để đánh giá.',
                  style: NordicTypography.bodySmall,
                ),
                NordicButton(
                  key: Key('rate-${assignment.assignmentId}'),
                  label: 'Đánh giá người làm',
                  onPressed: () => _open(
                    RatingRoutes.ratings,
                    RatingScreenArgs(
                      role: RatingRole.customer,
                      assignmentId: assignment.assignmentId,
                      ratedName: workerName(assignment),
                      workerId: assignment.workerId,
                    ),
                  ),
                ),
              ],
            ),
          ],
        );
      }
    }
    final awaiting = progress.assignments.where(
      (a) => a.assignmentStatus == AssignmentStatus.awaitingAcceptance,
    );
    if (awaiting.isNotEmpty) {
      final assignment = awaiting.first;
      return BottomActionBar(
        children: [
          Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              NordicButton(
                key: Key('accept-${assignment.assignmentId}'),
                label: 'Xác nhận nghiệm thu',
                onPressed: () => _open(
                  WorkerRoutes.customerAcceptance,
                  CustomerAcceptanceArgs(
                    assignmentId: assignment.assignmentId,
                    acceptance: assignment.acceptance,
                  ),
                ),
              ),
              NordicButton(
                key: Key('view-photos-${assignment.assignmentId}'),
                label: 'Xem ảnh',
                variant: NordicButtonVariant.ghost,
                onPressed: () => _open(
                  WorkerRoutes.customerAcceptance,
                  CustomerAcceptanceArgs(
                    assignmentId: assignment.assignmentId,
                    acceptance: assignment.acceptance,
                  ),
                ),
              ),
            ],
          ),
        ],
      );
    }
    if (order.orderStatus == OrderStatus.assigned &&
        canExtend(progress) &&
        !_extOpen) {
      return BottomActionBar(
        children: [
          NordicButton(
            key: const Key('open-extension'),
            label: 'Làm lần 2',
            onPressed: _openExtension,
          ),
        ],
      );
    }
    return null;
  }

  /// The 7 steps as a vertical list (MOB-ORDER-02): 7 labels do not fit side by side on a phone. Done steps are ticked;
  /// the current one is bold with its hint. "Có người làm", "đã đến" and "đang làm" are milestones already reached when
  /// they are current, so they are ticked too; the waiting steps (pay, find, acceptance) are a highlighted ring.
  Widget _timeline(String status, List<ProgressAssignment> assignments) {
    final steps = orderTimeline(status, assignments);
    if (steps.length == 1 && steps.single.cancelled) {
      return Row(
        key: const Key('order-timeline'),
        children: [
          Container(
            width: 28,
            height: 28,
            decoration: const BoxDecoration(
              color: NordicColors.error,
              shape: BoxShape.circle,
            ),
            child: const Icon(
              Icons.close_rounded,
              size: 18,
              color: NordicColors.textInverse,
            ),
          ),
          const SizedBox(width: NordicSpace.sm),
          Text(
            steps.single.label,
            style: NordicTypography.titleSmall.copyWith(
              color: NordicColors.error,
            ),
          ),
        ],
      );
    }
    return Column(
      key: const Key('order-timeline'),
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        for (var i = 0; i < steps.length; i++)
          _timelineRow(
            steps[i],
            reached: steps[i].done || (steps[i].current && i >= 2 && i <= 4),
            last: i == steps.length - 1,
            nextReached: i + 1 < steps.length && steps[i + 1].done,
          ),
      ],
    );
  }

  Widget _timelineRow(
    TimelineStep step, {
    required bool reached,
    required bool last,
    required bool nextReached,
  }) {
    final waiting = step.current && !reached;
    final Widget dot = reached
        ? Container(
            width: 24,
            height: 24,
            decoration: const BoxDecoration(
              color: NordicColors.primary,
              shape: BoxShape.circle,
            ),
            child: const Icon(
              Icons.check,
              size: 16,
              color: NordicColors.textInverse,
            ),
          )
        : Container(
            width: 24,
            height: 24,
            decoration: BoxDecoration(
              color: waiting
                  ? NordicColors.primaryContainer
                  : NordicColors.surface,
              shape: BoxShape.circle,
              border: Border.all(
                color: waiting ? NordicColors.primary : NordicColors.border,
                width: waiting ? 2 : 1.5,
              ),
            ),
          );
    return IntrinsicHeight(
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Column(
            children: [
              dot,
              if (!last)
                Expanded(
                  child: Container(
                    width: 2,
                    margin: const EdgeInsets.symmetric(vertical: 2),
                    color: nextReached
                        ? NordicColors.primary
                        : NordicColors.border,
                  ),
                ),
            ],
          ),
          const SizedBox(width: NordicSpace.sm),
          Expanded(
            child: Padding(
              padding: EdgeInsets.only(
                top: 2,
                bottom: last ? 0 : NordicSpace.sm,
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    step.label,
                    style: step.current
                        ? NordicTypography.titleSmall
                        : NordicTypography.bodyRegular.copyWith(
                            color: step.done
                                ? NordicColors.textTitle
                                : NordicColors.textSecondary,
                          ),
                  ),
                  if (step.hint != null) ...[
                    const SizedBox(height: NordicSpace.xxs),
                    Text(
                      step.hint!,
                      key: const Key('order-timeline-hint'),
                      style: NordicTypography.bodySmall,
                    ),
                  ],
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }

  /// Where the job is (MOB-ORDER-02, like the Web "Địa điểm" card): label, street and ward, total area.
  Widget _addressCard(CustomerAddress a) {
    return NordicCard(
      key: const Key('order-address'),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Icon(
            Icons.location_on_outlined,
            size: 20,
            color: NordicColors.primary,
          ),
          const SizedBox(width: NordicSpace.sm),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(a.label, style: NordicTypography.titleSmall),
                const SizedBox(height: NordicSpace.xxs),
                Text(
                  '${a.addressLine} · ${a.district} · ${formatArea(a.totalAreaM2)}',
                  key: const Key('order-address-line'),
                  style: NordicTypography.bodySmall,
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  List<Widget> _assignments(BookingOrder order, OrderProgress progress) {
    if (progress.assignments.isEmpty) {
      return [
        Text(
          switch (order.orderStatus) {
            OrderStatus.pendingPayment =>
              'Người làm được tìm sau khi bạn thanh toán.',
            _ => 'Đang tìm người làm cho đơn của bạn. Màn hình tự cập nhật.',
          },
          key: const Key('no-assignments'),
          style: NordicTypography.bodySmall,
        ),
      ];
    }
    final firstAcceptance = progress.assignments
        .where((a) => a.assignmentStatus == AssignmentStatus.awaitingAcceptance)
        .firstOrNull;
    final firstRating = order.orderStatus == OrderStatus.completed
        ? progress.assignments
              .where((a) => _ratingEligibility[a.assignmentId] == true)
              .firstOrNull
        : null;
    return [
      for (final a in progress.assignments) ...[
        NordicCard(
          key: Key('assignment-${a.assignmentId}'),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                children: [
                  AvatarInitials(workerName(a)),
                  const SizedBox(width: NordicSpace.sm),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(workerName(a), style: NordicTypography.titleSmall),
                        if (ratingText(a.workerRatingAvg).isNotEmpty)
                          Row(
                            children: [
                              const Icon(
                                Icons.star,
                                size: 16.0,
                                color: NordicColors.accentAmber,
                              ),
                              const SizedBox(width: 2.0),
                              Flexible(
                                child: Text(
                                  ratingText(a.workerRatingAvg),
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                  style: NordicTypography.bodySmall,
                                ),
                              ),
                            ],
                          ),
                      ],
                    ),
                  ),
                  if (_favoriteIds != null) _favoriteHeart(a),
                  const SizedBox(width: NordicSpace.xs),
                  // Full label width ("Chờ bạn nghiệm thu"); the name column gives way first.
                  ConstrainedBox(
                    constraints: const BoxConstraints(maxWidth: 180.0),
                    child: StatusPill(
                      assignmentStatusLabel(a.assignmentStatus),
                      textKey: Key('assignment-status-${a.assignmentId}'),
                      dot: true,
                      tone: switch (a.assignmentStatus) {
                        AssignmentStatus.awaitingAcceptance => PillTone.warn,
                        AssignmentStatus.cancelledByWorker ||
                        AssignmentStatus.absent ||
                        AssignmentStatus.incident => PillTone.bad,
                        _ => PillTone.ok,
                      },
                    ),
                  ),
                ],
              ),
              // ACCEPT-02: the phase and the deadline of the 24-hour acceptance window, from the server.
              if (customerAcceptanceView(a.acceptance, DateTime.now())
                  case final view?
                  when a.acceptance?.phase != AcceptancePhase.inProgress) ...[
                const SizedBox(height: NordicSpace.sm),
                Container(
                  key: Key('acceptance-${a.assignmentId}'),
                  padding: const EdgeInsets.all(NordicSpace.sm),
                  decoration: BoxDecoration(
                    color: a.acceptance?.phase == AcceptancePhase.completed
                        ? NordicColors.primaryFixed
                        : NordicColors.amberSoft,
                    borderRadius: NordicRadius.tileAll,
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        view.title,
                        key: Key('acceptance-title-${a.assignmentId}'),
                        style: NordicTypography.titleSmall,
                      ),
                      const SizedBox(height: 2.0),
                      Text(
                        view.note,
                        key: Key('acceptance-note-${a.assignmentId}'),
                        style: NordicTypography.bodySmall,
                      ),
                    ],
                  ),
                ),
              ],
              if (a.assignmentStatus == AssignmentStatus.awaitingAcceptance &&
                  a.assignmentId != firstAcceptance?.assignmentId) ...[
                const SizedBox(height: NordicSpace.sm),
                NordicButton(
                  key: Key('accept-${a.assignmentId}'),
                  label: 'Xác nhận nghiệm thu',
                  variant: NordicButtonVariant.secondary,
                  onPressed: () => _open(
                    WorkerRoutes.customerAcceptance,
                    CustomerAcceptanceArgs(
                      assignmentId: a.assignmentId,
                      acceptance: a.acceptance,
                    ),
                  ),
                ),
              ],
              if (a.assignmentStatus == AssignmentStatus.completed &&
                  _ratingEligibility[a.assignmentId] == true &&
                  a.assignmentId != firstRating?.assignmentId) ...[
                const SizedBox(height: NordicSpace.sm),
                NordicButton(
                  key: Key('rate-${a.assignmentId}'),
                  label: 'Đánh giá người làm',
                  variant: NordicButtonVariant.secondary,
                  onPressed: () => _open(
                    RatingRoutes.ratings,
                    RatingScreenArgs(
                      role: RatingRole.customer,
                      assignmentId: a.assignmentId,
                      ratedName: workerName(a),
                      workerId: a.workerId,
                    ),
                  ),
                ),
              ],
            ],
          ),
        ),
        const SizedBox(height: NordicSpace.xs),
      ],
    ];
  }

  /// The rows of ui-templates "Khi thợ đang làm": what the customer can do now, each opening its screen.
  List<GroupedListItem> _actions(BookingOrder order, OrderProgress progress) {
    final named = progress.assignments.length > 1;
    final awaiting = progress.assignments.where(
      (a) => a.assignmentStatus == AssignmentStatus.awaitingAcceptance,
    );
    final primaryAcceptance = awaiting.isEmpty
        ? null
        : awaiting.first.assignmentId;
    return [
      if (primaryAcceptance != null &&
          canExtend(progress) &&
          order.orderStatus == OrderStatus.assigned &&
          !_extOpen)
        GroupedListItem(
          key: const Key('open-extension'),
          icon: Icons.more_time_outlined,
          title: 'Làm lần 2',
          onTap: _openExtension,
        ),
      for (final a in progress.assignments)
        if (a.assignmentStatus == AssignmentStatus.completed)
          GroupedListItem(
            key: Key('review-acceptance-${a.assignmentId}'),
            icon: Icons.photo_library_outlined,
            title: 'Xem lại nghiệm thu',
            subtitle: named ? workerName(a) : 'Ảnh trước và sau khi dọn',
            onTap: () => _open(
              WorkerRoutes.customerAcceptance,
              CustomerAcceptanceArgs(
                assignmentId: a.assignmentId,
                completed: true,
              ),
            ),
          ),
      if (order.serviceTier == ServiceTier.economy)
        for (final a in progress.assignments)
          if (a.assignmentStatus == AssignmentStatus.completed)
            GroupedListItem(
              key: Key('rebook-${a.assignmentId}'),
              icon: Icons.replay_rounded,
              title: 'Đặt lại với ${workerName(a)}',
              subtitle: 'Cùng gói, cùng địa chỉ',
              onTap: () => _rebook(order, a),
            ),
      if (canDispute(progress))
        GroupedListItem(
          key: const Key('dispute-order'),
          icon: Icons.flag_outlined,
          title: 'Khiếu nại về đơn này',
          subtitle: 'Trong 24 giờ sau khi xong ca',
          onTap: () => _open(
            DisputeRoutes.disputes,
            DisputeScreenArgs(
              role: DisputeRole.customer,
              orderId: order.orderId,
            ),
          ),
        ),
    ];
  }

  Widget _extensionCard(OrderExtension extension) => NordicCard(
    key: const Key('extension-card'),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            const Icon(Icons.more_time_outlined, color: NordicColors.primary),
            const SizedBox(width: NordicSpace.xs),
            const Expanded(
              child: Text('Làm lần 2', style: NordicTypography.titleSmall),
            ),
            ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 180.0),
              child: StatusPill(
                extensionStatusLabel(extension.extStatus),
                textKey: const Key('extension-status'),
                tone: switch (extension.extStatus) {
                  ExtensionStatus.accepted => PillTone.ok,
                  ExtensionStatus.pendingPayment ||
                  ExtensionStatus.paid => PillTone.warn,
                  _ => PillTone.bad,
                },
              ),
            ),
          ],
        ),
        const SizedBox(height: NordicSpace.xs),
        Text(
          'Thêm ${hoursText(extension.extraHours)} · ${formatVnd(extension.extraAmount)}',
          key: const Key('extension-summary'),
          style: NordicTypography.titleSmall,
        ),
        if (extension.extStatus == ExtensionStatus.pendingPayment) ...[
          const SizedBox(height: NordicSpace.sm),
          NordicButton(
            key: const Key('pay-extension'),
            label: 'Thanh toán ${formatVnd(extension.extraAmount)}',
            width: double.infinity,
            onPressed: () => _pay(extensionId: extension.extensionId),
          ),
        ],
      ],
    ),
  );

  Widget _extensionForm(List<ProgressAssignment> workers) {
    final options = _options;
    final selected = workers.any((a) => a.assignmentId == _extAssignmentId)
        ? _extAssignmentId
        : workers.first.assignmentId;
    return NordicCard(
      key: const Key('extension-form'),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text('Làm lần 2', style: NordicTypography.titleSmall),
          const SizedBox(height: NordicSpace.xxs),
          const Text(
            'Giá hiện sau khi gửi. Người làm được hỏi sau khi bạn thanh toán.',
            style: NordicTypography.bodySmall,
          ),
          if (workers.length > 1) ...[
            const SizedBox(height: NordicSpace.sm),
            const Text('Người làm', style: NordicTypography.titleSmall),
            const SizedBox(height: 6.0),
            Wrap(
              spacing: 8.0,
              runSpacing: 8.0,
              children: [
                for (final a in workers)
                  ChoiceChip(
                    key: Key('extension-worker-${a.assignmentId}'),
                    label: Text(workerName(a)),
                    selected: a.assignmentId == selected,
                    onSelected: (_) =>
                        setState(() => _extAssignmentId = a.assignmentId),
                  ),
              ],
            ),
          ],
          const SizedBox(height: NordicSpace.sm),
          const Text('Số giờ thêm', style: NordicTypography.titleSmall),
          const SizedBox(height: 6.0),
          if (_extLoading) const LoadingStateWidget(message: 'Đang tải…'),
          if (options != null)
            Wrap(
              spacing: 8.0,
              runSpacing: 8.0,
              children: [
                for (final h in extraHourOptions(options.shiftMaxHours))
                  ChoiceChip(
                    key: Key('hours-$h'),
                    label: Text(hoursText(h)),
                    selected: _extHours == h,
                    onSelected: (_) => setState(() => _extHours = h),
                  ),
              ],
            ),
          if (_extError != null) ...[
            const SizedBox(height: NordicSpace.xs),
            Text(
              _extError!,
              key: const Key('extension-error'),
              style: NordicTypography.bodySmall.copyWith(
                color: NordicColors.error,
              ),
            ),
          ],
          const SizedBox(height: NordicSpace.sm),
          if (options != null)
            NordicButton(
              key: const Key('submit-extension'),
              label: 'Gửi yêu cầu',
              width: double.infinity,
              isLoading: _extSubmitting,
              onPressed: () => _requestExtension(workers),
            )
          else if (!_extLoading)
            NordicButton(
              label: 'Thử lại',
              width: double.infinity,
              variant: NordicButtonVariant.secondary,
              onPressed: _openExtension,
            ),
        ],
      ),
    );
  }

  /// Decisions D35: the helper cancelled; a substitute is searched for until [until], then the order is refunded 100 %.
  Widget _substituteSearchCard(DateTime until) {
    return Container(
      key: const Key('substitute-search'),
      width: double.infinity,
      padding: const EdgeInsets.all(NordicSpace.md),
      decoration: const BoxDecoration(
        color: NordicColors.amberSoft,
        borderRadius: NordicRadius.cardAll,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Đang tìm người làm thay',
            style: NordicTypography.titleSmall.copyWith(
              color: NordicColors.onAmberSoft,
            ),
          ),
          const SizedBox(height: NordicSpace.xxs),
          Text(
            'Người giúp việc đã huỷ ca. Hệ thống đang tìm người khác tới ${_localTime(until)}. '
            'Nếu không tìm được, đơn tự huỷ và bạn được hoàn tiền 100 %. '
            'Bạn cũng có thể huỷ đơn ngay, vẫn được hoàn 100 %.',
            style: NordicTypography.bodySmall.copyWith(
              color: NordicColors.onAmberSoft,
            ),
          ),
        ],
      ),
    );
  }

  Widget _cancelSection() {
    if (!_cancelOpen) {
      return NordicButton(
        key: const Key('open-cancel'),
        label: 'Huỷ đơn',
        width: double.infinity,
        variant: NordicButtonVariant.danger,
        onPressed: () => setState(() {
          _cancelOpen = true;
          _cancelError = null;
        }),
      );
    }
    return NordicCard(
      key: const Key('cancel-form'),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text('Huỷ đơn', style: NordicTypography.titleSmall),
          const SizedBox(height: NordicSpace.xs),
          NordicTextInput(
            key: const Key('cancel-reason-input'),
            label: 'Lý do huỷ',
            controller: _reason,
            maxLines: 3,
          ),
          if (_cancelError != null) ...[
            const SizedBox(height: NordicSpace.xs),
            Text(
              _cancelError!,
              key: const Key('cancel-error'),
              style: NordicTypography.bodySmall.copyWith(
                color: NordicColors.error,
              ),
            ),
          ],
          const SizedBox(height: NordicSpace.sm),
          NordicButton(
            key: const Key('submit-cancel'),
            label: 'Xác nhận huỷ',
            width: double.infinity,
            variant: NordicButtonVariant.danger,
            isLoading: _cancelling,
            onPressed: _cancel,
          ),
          const SizedBox(height: NordicSpace.xxs),
          NordicButton(
            label: 'Không huỷ nữa',
            width: double.infinity,
            variant: NordicButtonVariant.ghost,
            onPressed: () => setState(() => _cancelOpen = false),
          ),
        ],
      ),
    );
  }
}
