import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/network/api_response.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/booking/models/booking_models.dart';
import 'package:mobile/features/booking/services/order_service.dart';
import 'package:mobile/features/booking/services/fallback_service.dart';
import 'package:mobile/features/booking/services/booking_service.dart';
import 'package:mobile/features/booking/screens/booking_screen.dart';
import 'package:mobile/features/booking/screens/order_detail_screen.dart';
import 'package:mobile/features/booking/screens/order_history_screen.dart';
import 'package:mobile/features/customers/models/favorite_worker.dart';
import 'package:mobile/features/customers/services/customer_profile_service.dart';
import 'package:mobile/features/customers/services/customer_address_service.dart';
import 'package:mobile/features/customers/services/favorite_worker_service.dart';
import 'package:mobile/features/customers/screens/favorite_workers_screen.dart';
import 'package:mobile/features/customers/screens/customer_profile_screen.dart';
import 'package:mobile/features/customers/screens/address_list_screen.dart';
import 'package:mobile/features/customers/screens/address_form_screen.dart';
import 'package:mobile/features/home/customer_home_tab.dart';
import 'package:mobile/features/home/customer_shell.dart';
import 'package:mobile/features/home/customer_account_tab.dart';
import 'package:mobile/features/payments/models/payment_models.dart';
import 'package:mobile/features/payments/services/payments_service.dart';
import 'package:mobile/features/payments/screens/payment_screen.dart';
import 'package:mobile/features/ratings/models/rating_models.dart';
import 'package:mobile/features/ratings/services/ratings_service.dart';
import 'package:mobile/features/ratings/screens/rating_screen.dart';
import 'package:mobile/features/disputes/models/dispute_models.dart';
import 'package:mobile/features/disputes/services/disputes_service.dart';
import 'package:mobile/features/disputes/services/evidence_uploader.dart';
import 'package:mobile/features/disputes/screens/dispute_create_screen.dart';
import 'package:mobile/features/disputes/screens/dispute_screen.dart';
import 'package:mobile/features/workers/models/job_photo_models.dart';
import 'package:mobile/features/workers/services/workers_photos_service.dart';
import 'package:mobile/features/workers/services/customer_acceptance_service.dart';
import 'package:mobile/features/workers/screens/customer_acceptance_screen.dart';

const _options = BookingOptions(
  serviceTiers: ['ECONOMY', 'PREMIUM'],
  shifts: [
    ShiftOption(
      shiftCode: 'SHIFT_MORNING',
      startLocal: '08:00',
      endLocal: '12:00',
    ),
  ],
  premiumMinLeadHours: 4,
  shiftMaxHours: 4,
  standardMaxAreaM2: 80,
  paymentQrExpiryMinutes: 15,
  sandbox: true,
);

BookingOrder _order(String status, {String? cancelReason}) => BookingOrder(
  orderId: 100,
  orderCode: 'GV261014ABC123',
  serviceTier: ServiceTier.economy,
  addressId: 1,
  scheduledDate: '2026-10-15',
  shiftCode: 'SHIFT_MORNING',
  shiftStartAt: DateTime.utc(2026, 10, 15, 1),
  shiftEndAt: DateTime.utc(2026, 10, 15, 5),
  areaSnapshotM2: 50,
  requiredWorkers: 1,
  totalAmount: 260000,
  orderStatus: status,
  customerNote: null,
  cancelReason: cancelReason,
  paymentDeadlineAt: null,
  createdAt: null,
);

ProgressAssignment _worker(
  int id,
  String status, {
  String? name = 'Nguyễn Thị Lan',
}) => ProgressAssignment(
  assignmentId: id,
  assignmentSeq: 1,
  workerId: id + 50,
  workerName: name,
  workerRatingAvg: 4.8,
  assignmentStatus: status,
  acceptedAt: null,
  completedAt: null,
);

OrderExtension _extension(
  String status, {
  double hours = 1.5,
  int amount = 97500,
}) => OrderExtension(
  extensionId: 9,
  orderId: 100,
  workerId: 57,
  extraHours: hours,
  extraAmount: amount,
  extStatus: status,
  workerDecision: 'PENDING',
  requestedAt: null,
  decidedAt: null,
);

OrderSummary _summary(int id, String status) => OrderSummary(
  orderId: id,
  orderCode: 'GV$id',
  serviceTier: ServiceTier.economy,
  scheduledDate: '2026-10-15',
  shiftCode: 'SHIFT_MORNING',
  requiredWorkers: 1,
  totalAmount: 260000,
  orderStatus: status,
  createdAt: null,
);

class FakeOrderService implements IOrderService {
  final List<String?> statusesAsked = [];
  BookingOptions options = _options;
  String status = OrderStatus.dispatching;
  String? cancelReason;
  List<ProgressAssignment> workers = [];
  OrderExtension? extension;

  Object? loadError;
  Object? optionsError;
  Object? cancelError;
  Object? extensionError;
  Completer<BookingOrder>? cancelGate;

  int orderReads = 0;
  final cancels = <String>[];
  final extensions = <({int assignmentId, double hours})>[];

  // History
  List<OrderSummary> all = [];
  Object? listError;
  final pagesAsked = <int>[];

  @override
  Future<BookingOrder> getOrder(int orderId) {
    orderReads++;
    return loadError != null
        ? Future.error(loadError!)
        : Future.value(_order(status, cancelReason: cancelReason));
  }

  @override
  Future<OrderProgress> getProgress(int orderId) => loadError != null
      ? Future.error(loadError!)
      : Future.value(
          OrderProgress(
            orderId: orderId,
            orderStatus: status,
            requiredWorkers: 1,
            assignments: workers,
            extension: extension,
          ),
        );

  @override
  Future<BookingOptions> getOptions() => optionsError != null
      ? Future.error(optionsError!)
      : Future.value(options);

  @override
  Future<BookingOrder> cancelOrder(int orderId, String reason) {
    cancels.add(reason);
    if (cancelGate != null) return cancelGate!.future;
    if (cancelError != null) return Future.error(cancelError!);
    status = OrderStatus.cancelled;
    cancelReason = reason;
    return Future.value(_order(status, cancelReason: reason));
  }

  @override
  Future<OrderExtension> requestExtension({
    required int orderId,
    required int assignmentId,
    required double extraHours,
  }) {
    extensions.add((assignmentId: assignmentId, hours: extraHours));
    if (extensionError != null) return Future.error(extensionError!);
    extension = _extension(
      ExtensionStatus.pendingPayment,
      hours: extraHours,
      amount: 65000,
    );
    return Future.value(extension!);
  }

  @override
  Future<OrderPage> listOrders({
    int page = 1,
    int pageSize = 20,
    String? status,
  }) {
    pagesAsked.add(page);
    statusesAsked.add(status);
    if (listError != null) return Future.error(listError!);
    final matching = status == null
        ? all
        : all.where((o) => o.orderStatus == status).toList();
    final items = matching.skip((page - 1) * pageSize).take(pageSize).toList();
    return Future.value(
      OrderPage(
        items: items,
        page: page,
        pageSize: pageSize,
        total: matching.length,
      ),
    );
  }
}

final _start = DateTime.utc(2026, 10, 14, 3);

Payment _payment({
  String status = 'PENDING',
  String? qr = 'fake-qr://FAKE-1',
  String? payUrl = 'https://sandbox.invalid/pay/FAKE-1',
  String purpose = 'ORDER',
  int amount = 260000,
  String gateway = 'FAKE',
}) => Payment(
  paymentId: 5,
  purpose: purpose,
  orderId: purpose == 'ORDER' ? 42 : null,
  extensionId: purpose == 'EXTENSION' ? 9 : null,
  gateway: gateway,
  amount: amount,
  txnStatus: status,
  qrPayload: status == 'PENDING' ? qr : null,
  payUrl: payUrl,
  expiresAt: _start.add(const Duration(minutes: 15)),
  paidAt: status == 'SUCCESS' ? _start.add(const Duration(minutes: 1)) : null,
  createdAt: _start,
  sandbox: true,
);

class FakePaymentsService implements IPaymentsService {
  Object? createError;
  Completer<Payment>? createGate;
  Payment created = _payment();
  final createdOrders = <int>[];
  final createdExtensions = <int>[];

  /// What the next status checks answer, in order; the last one repeats.
  final List<Object> polls = [];
  int pollCalls = 0;

  @override
  Future<Payment> createOrderQr(int orderId) {
    createdOrders.add(orderId);
    if (createGate != null) return createGate!.future;
    return createError != null
        ? Future.error(createError!)
        : Future.value(created);
  }

  @override
  Future<Payment> createExtensionQr(int extensionId) {
    createdExtensions.add(extensionId);
    return createError != null
        ? Future.error(createError!)
        : Future.value(created);
  }

  @override
  Future<Payment> getPayment(int paymentId) {
    final answer = polls.isEmpty
        ? created
        : polls[pollCalls < polls.length ? pollCalls : polls.length - 1];
    pollCalls++;
    return answer is Payment ? Future.value(answer) : Future.error(answer);
  }
}

class FakePhotosService extends WorkersPhotosService {
  List<JobPhotoDto> photos = [];
  ApiException? failure;
  int reads = 0;

  FakePhotosService()
    : super(
        apiClient: ApiClient(
          baseUrl: 'http://api.test',
          httpClient: FakeHttp(),
        ),
      );

  @override
  Future<ApiResponse<List<JobPhotoDto>>> getPhotos(int assignmentId) async {
    reads++;
    if (failure != null) throw failure!;
    return ApiResponse<List<JobPhotoDto>>(
      success: true,
      message: 'OK',
      data: photos,
    );
  }
}

class FakeAddresses implements IBookingAddressSource {
  List<BookingAddress> list;
  Object? error;
  int calls = 0;

  FakeAddresses([List<BookingAddress>? list])
    : list =
          list ??
          const [
            BookingAddress(
              addressId: 1,
              label: 'Nhà riêng',
              addressLine: '1 Lê Lợi',
              totalAreaM2: 50,
              isDefault: true,
            ),
            BookingAddress(
              addressId: 2,
              label: 'Nhà bố mẹ',
              addressLine: '9 Hai Bà Trưng',
              totalAreaM2: 90,
            ),
          ];

  @override
  Future<List<BookingAddress>> getAddresses() {
    calls++;
    return error != null ? Future.error(error!) : Future.value(list);
  }
}

/// The favorites and their free shifts; empty by default, so the section stays hidden and nothing goes to the network.
class FakeFavorites implements IBookingFavoriteSource {
  List<FavoriteWorker> list;
  Set<String> free;
  final freeAsked = <int>[];

  FakeFavorites([this.list = const [], this.free = const {}]);

  @override
  Future<List<FavoriteWorker>> getFavorites() => Future.value(list);

  @override
  Future<Set<String>> getFreeShifts(int workerId) {
    freeAsked.add(workerId);
    return Future.value(free);
  }
}

class FakeRebookAvailability implements IRebookAvailabilitySource {
  final requested = <(int, int)>[];

  @override
  Future<Set<String>> getFreeShifts(int orderId, int workerId) async {
    requested.add((orderId, workerId));
    return {'2026-10-15|SHIFT_MORNING'};
  }
}

class FakeHttp implements HttpClient {
  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

class PreviewFallback implements IFallbackService {
  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

class PreviewRebook implements IRebookAvailabilitySource {
  @override
  Future<Set<String>> getFreeShifts(int orderId, int workerId) async => {};
}

class PreviewBooking implements IBookingService {
  @override
  Future<BookingOptions> getOptions() async => _options;
  @override
  Future<PriceQuote> getQuote({
    required int addressId,
    required String serviceTier,
  }) async => PriceQuote(
    addressId: addressId,
    serviceTier: serviceTier,
    totalAreaM2: 50,
    areaBracket: 'FROM_31_TO_80',
    requiredWorkers: 1,
    unitPrice: 260000,
    totalAmount: 260000,
    currency: 'VND',
  );
  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

class PreviewRatings implements IRatingsService {
  @override
  Future<RatingWindow> getWindow(RatingRole role, int assignmentId) async =>
      RatingWindow(
        assignmentId: assignmentId,
        canRate: true,
        reason: 'OPEN',
        secondsRemaining: 86400,
      );
  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

class PreviewDisputes implements IDisputesService {
  @override
  Future<List<Dispute>> listMine(DisputeRole role) async => [];
  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

class PreviewUploader implements IEvidenceUploader {
  @override
  bool get isAvailable => true;
  @override
  Future<String?> pickAndUpload() async => null;
}

Widget customerPreview(
  String screen, {
  double textScale = 1,
  bool keyboard = false,
  bool reducedMotion = false,
}) {
  final orders = FakeOrderService();
  final now = DateTime.utc(2026, 10, 14, 3);
  final profile = CustomerProfileService(isMock: true, httpClient: FakeHttp());
  final addresses = CustomerAddressService(
    isMock: true,
    httpClient: FakeHttp(),
  );
  final favorites = FavoriteWorkerService(isMock: true, httpClient: FakeHttp());
  orders.all =
      screen == 'orders-empty' ||
          screen == 'home-empty' ||
          screen == 'home-no-address'
      ? []
      : [
          _summary(100, OrderStatus.assigned),
          _summary(101, OrderStatus.cancelled),
          _summary(102, OrderStatus.completed),
        ];
  final homeAddresses = screen == 'home-no-address'
      ? FakeAddresses([])
      : FakeAddresses();
  final state = switch (screen) {
    'detail-pending' => OrderStatus.pendingPayment,
    'detail-assigned' => OrderStatus.assigned,
    'detail-completed' => OrderStatus.completed,
    'detail-cancelled' => OrderStatus.cancelled,
    _ => OrderStatus.assigned,
  };
  orders.status = state;
  if (state == OrderStatus.assigned || state == OrderStatus.completed) {
    orders.workers = [
      _worker(
        7,
        state == OrderStatus.completed
            ? AssignmentStatus.completed
            : AssignmentStatus.assigned,
      ),
    ];
  }
  final Widget child = switch (screen) {
    'home' || 'home-empty' || 'home-no-address' => CustomerHomeTab(
      orders: orders,
      booking: PreviewBooking(),
      addresses: homeAddresses,
      favorites: favorites,
      profile: profile,
      clock: () => now,
    ),
    'orders' ||
    'orders-cancelled' ||
    'orders-empty' => OrderHistoryScreen(service: orders, clock: () => now),
    'detail-pending' ||
    'detail-assigned' ||
    'detail-completed' ||
    'detail-cancelled' => OrderDetailScreen(
      args: const OrderDetailArgs(orderId: 100),
      service: orders,
      ratings: PreviewRatings(),
      fallbackService: PreviewFallback(),
      pollInterval: const Duration(hours: 1),
    ),
    'favorites' => FavoriteWorkersScreen(service: favorites),
    'account' => CustomerAccountTab(
      onSignOut: () {},
      profile: profile,
      addresses: addresses,
      orders: orders,
    ),
    'booking' => BookingScreen(
      service: PreviewBooking(),
      addresses: FakeAddresses(),
      favorites: FakeFavorites(),
      rebookAvailability: PreviewRebook(),
      now: () => now,
    ),
    'payment' => PaymentScreen(
      args: const PaymentScreenArgs(orderId: 42, orderCode: 'GV261014ABC123'),
      service: FakePaymentsService(),
      now: () => _start,
      pollInterval: const Duration(hours: 1),
    ),
    'acceptance' => CustomerAcceptanceScreen(
      assignmentId: 55,
      photosService: FakePhotosService(),
      acceptanceService: CustomerAcceptanceService(
        apiClient: ApiClient(httpClient: FakeHttp()),
      ),
    ),
    'rating' => RatingScreen(
      role: RatingRole.customer,
      assignmentId: 7,
      ratedName: 'Nguyễn Thị Lan',
      service: PreviewRatings(),
    ),
    'dispute' => DisputeCreateScreen(
      role: DisputeRole.customer,
      orderId: 42,
      service: PreviewDisputes(),
      uploader: PreviewUploader(),
    ),
    'dispute-list' => DisputeScreen(
      role: DisputeRole.customer,
      service: PreviewDisputes(),
      uploader: PreviewUploader(),
    ),
    'profile' => CustomerProfileScreen(service: profile),
    'addresses' => AddressListScreen(addressService: addresses),
    'address-form' => AddressFormScreen(addressService: addresses),
    _ => const SizedBox.shrink(),
  };
  final int? tab = switch (screen) {
    'home' || 'home-empty' || 'home-no-address' => 0,
    'orders' || 'orders-cancelled' || 'orders-empty' => 1,
    'favorites' => 2,
    'account' => 3,
    _ => null,
  };
  final tabs = List<Widget>.filled(4, const SizedBox.shrink());
  if (tab != null) tabs[tab] = child;
  return MaterialApp(
    debugShowCheckedModeBanner: false,
    theme: NordicTheme.lightTheme,
    builder: (context, child) => MediaQuery(
      data: MediaQuery.of(context).copyWith(
        textScaler: TextScaler.linear(textScale),
        disableAnimations: reducedMotion,
        viewInsets: keyboard
            ? const EdgeInsets.only(bottom: 280)
            : EdgeInsets.zero,
      ),
      child: child!,
    ),
    home: tab == null
        ? child
        : CustomerShell(onSignOut: () {}, initialTab: tab, tabs: tabs),
  );
}

void main() =>
    runApp(customerPreview(Uri.base.queryParameters['screen'] ?? 'home'));
