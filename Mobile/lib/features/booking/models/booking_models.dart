import '../../workers/logic/acceptance_logic.dart';

// Models of the customer booking screens, hand-written from .spec/contracts/booking.md (sections 2, 3.1 to 3.8) until gate G4.
// Every amount is whole VND computed by the server; the device never recomputes money.

/// `serviceTier` values of the contract.
class ServiceTier {
  static const economy = 'ECONOMY';
  static const premium = 'PREMIUM';

  ServiceTier._();
}

/// `orderStatus` values of the contract (booking.md 1.1).
class OrderStatus {
  static const pendingPayment = 'PENDING_PAYMENT';
  static const paid = 'PAID';
  static const dispatching = 'DISPATCHING';
  static const assigned = 'ASSIGNED';
  static const completed = 'COMPLETED';
  static const cancelled = 'CANCELLED';

  /// BR-02 fallback: one worker found, the second seat has nobody; waits for the customer (dispatch.md 3.2).
  static const awaitingFallback = 'AWAITING_FALLBACK';

  OrderStatus._();
}

int _int(dynamic v) => (v as num?)?.toInt() ?? 0;
double _double(dynamic v) => (v as num?)?.toDouble() ?? 0;
DateTime? _date(dynamic v) =>
    v is String ? DateTime.tryParse(v)?.toUtc() : null;

/// One shift of GET /api/v1/booking/options: the code and its local (Asia/Ho_Chi_Minh) start and end, "HH:mm".
class ShiftOption {
  final String shiftCode;
  final String startLocal;
  final String endLocal;

  const ShiftOption({
    required this.shiftCode,
    required this.startLocal,
    required this.endLocal,
  });

  factory ShiftOption.fromJson(Map<String, dynamic> json) => ShiftOption(
    shiftCode: json['shiftCode'] as String? ?? '',
    startLocal: json['startLocal'] as String? ?? '',
    endLocal: json['endLocal'] as String? ?? '',
  );
}

/// GET /api/v1/booking/options (booking.md 3.1): the app hard-codes none of these values.
class BookingOptions {
  final List<String> serviceTiers;
  final List<ShiftOption> shifts;
  final int premiumMinLeadHours;
  final int shiftMaxHours;
  final double standardMaxAreaM2;
  final int paymentQrExpiryMinutes;
  final bool sandbox;

  const BookingOptions({
    required this.serviceTiers,
    required this.shifts,
    required this.premiumMinLeadHours,
    required this.shiftMaxHours,
    required this.standardMaxAreaM2,
    required this.paymentQrExpiryMinutes,
    required this.sandbox,
  });

  factory BookingOptions.fromJson(Map<String, dynamic> json) => BookingOptions(
    serviceTiers: (json['serviceTiers'] as List? ?? const [])
        .map((e) => e.toString())
        .toList(),
    shifts: (json['shifts'] as List? ?? const [])
        .whereType<Map<String, dynamic>>()
        .map(ShiftOption.fromJson)
        .toList(),
    premiumMinLeadHours: _int(json['premiumMinLeadHours']),
    shiftMaxHours: _int(json['shiftMaxHours']),
    standardMaxAreaM2: _double(json['standardMaxAreaM2']),
    paymentQrExpiryMinutes: _int(json['paymentQrExpiryMinutes']),
    sandbox: json['sandbox'] as bool? ?? true,
  );
}

/// `PriceQuote` (booking.md section 2): the fixed price shown before the order is created.
class PriceQuote {
  final int addressId;
  final String serviceTier;
  final double totalAreaM2;
  final String areaBracket;
  final int requiredWorkers;
  final int unitPrice;
  final int totalAmount;
  final String currency;

  /// D35: an ECONOMY large house is done by ONE worker who may need a 2nd consecutive job (the next shift of the day).
  final bool isContinuousWork;

  const PriceQuote({
    required this.addressId,
    required this.serviceTier,
    required this.totalAreaM2,
    required this.areaBracket,
    required this.requiredWorkers,
    required this.unitPrice,
    required this.totalAmount,
    required this.currency,
    this.isContinuousWork = false,
  });

  factory PriceQuote.fromJson(Map<String, dynamic> json) => PriceQuote(
    addressId: _int(json['addressId']),
    serviceTier: json['serviceTier'] as String? ?? '',
    totalAreaM2: _double(json['totalAreaM2']),
    areaBracket: json['areaBracket'] as String? ?? '',
    requiredWorkers: _int(json['requiredWorkers']),
    unitPrice: _int(json['unitPrice']),
    totalAmount: _int(json['totalAmount']),
    currency: json['currency'] as String? ?? 'VND',
    isContinuousWork: json['isContinuousWork'] as bool? ?? false,
  );
}

/// `Order` (booking.md section 2). Times are UTC instants.
class BookingOrder {
  final int orderId;
  final String orderCode;
  final String serviceTier;
  final int addressId;

  /// "yyyy-MM-dd", the local date of the shift.
  final String scheduledDate;
  final String shiftCode;
  final DateTime? shiftStartAt;
  final DateTime? shiftEndAt;
  final double areaSnapshotM2;
  final int requiredWorkers;
  final int totalAmount;
  final String orderStatus;
  final String? customerNote;
  final String? cancelReason;
  final DateTime? paymentDeadlineAt;
  final DateTime? createdAt;

  /// D35: a large ECONOMY house (one worker, a possible 2nd consecutive job).
  final bool isContinuousWork;

  const BookingOrder({
    required this.orderId,
    required this.orderCode,
    required this.serviceTier,
    required this.addressId,
    required this.scheduledDate,
    required this.shiftCode,
    required this.shiftStartAt,
    required this.shiftEndAt,
    required this.areaSnapshotM2,
    required this.requiredWorkers,
    required this.totalAmount,
    required this.orderStatus,
    required this.customerNote,
    required this.cancelReason,
    required this.paymentDeadlineAt,
    required this.createdAt,
    this.isContinuousWork = false,
  });

  factory BookingOrder.fromJson(Map<String, dynamic> json) => BookingOrder(
    orderId: _int(json['orderId']),
    orderCode: json['orderCode'] as String? ?? '',
    serviceTier: json['serviceTier'] as String? ?? '',
    addressId: _int(json['addressId']),
    scheduledDate: json['scheduledDate'] as String? ?? '',
    shiftCode: json['shiftCode'] as String? ?? '',
    shiftStartAt: _date(json['shiftStartAt']),
    shiftEndAt: _date(json['shiftEndAt']),
    areaSnapshotM2: _double(json['areaSnapshotM2']),
    requiredWorkers: _int(json['requiredWorkers']),
    totalAmount: _int(json['totalAmount']),
    orderStatus: json['orderStatus'] as String? ?? '',
    customerNote: json['customerNote'] as String?,
    cancelReason: json['cancelReason'] as String?,
    paymentDeadlineAt: _date(json['paymentDeadlineAt']),
    createdAt: _date(json['createdAt']),
    isContinuousWork: json['isContinuousWork'] as bool? ?? false,
  );
}

/// What the customer chose; the body of POST /api/v1/booking/orders (booking.md 3.3).
class CreateOrderInput {
  final int addressId;
  final String serviceTier;

  /// "yyyy-MM-dd".
  final String scheduledDate;
  final String shiftCode;
  final String? customerNote;

  /// A favorite or a worker from a completed previous assignment (ECONOMY only).
  final int? preferredWorkerId;
  final int? previousOrderId;

  /// D35: the customer agreed that a large house is done by one worker who may work two consecutive jobs.
  final bool acceptsContinuousWork;

  const CreateOrderInput({
    required this.addressId,
    required this.serviceTier,
    required this.scheduledDate,
    required this.shiftCode,
    this.customerNote,
    this.preferredWorkerId,
    this.previousOrderId,
    this.acceptsContinuousWork = false,
  });

  Map<String, dynamic> toJson() => {
    'addressId': addressId,
    'serviceTier': serviceTier,
    'scheduledDate': scheduledDate,
    'shiftCode': shiftCode,
    'customerNote': customerNote,
    'preferredWorkerId': ?preferredWorkerId,
    'previousOrderId': ?previousOrderId,
    if (acceptsContinuousWork) 'acceptsContinuousWork': true,
  };
}

/// The address the booking screen needs; it comes from the Customers feature's address book.
class BookingAddress {
  final int addressId;
  final String label;
  final String addressLine;
  final double totalAreaM2;
  final bool isDefault;

  const BookingAddress({
    required this.addressId,
    required this.label,
    required this.addressLine,
    required this.totalAreaM2,
    this.isDefault = false,
  });
}

/// `assignmentStatus` values a customer can see (booking.md section 2; OFFERED, CANCELLED and REASSIGNED are never sent).
class AssignmentStatus {
  static const assigned = 'ASSIGNED';
  static const checkedIn = 'CHECKED_IN';
  static const inProgress = 'IN_PROGRESS';
  static const awaitingAcceptance = 'AWAITING_ACCEPTANCE';
  static const completed = 'COMPLETED';
  static const cancelledByWorker = 'CANCELLED_BY_WORKER';
  static const absent = 'ABSENT';
  static const incident = 'INCIDENT';

  AssignmentStatus._();
}

/// `extStatus` values (decision Q24 / B5).
class ExtensionStatus {
  static const pendingPayment = 'PENDING_PAYMENT';
  static const paid = 'PAID';
  static const accepted = 'ACCEPTED';
  static const declined = 'DECLINED';
  static const expired = 'EXPIRED';

  ExtensionStatus._();
}

/// What `GET /api/v1/dispatch/assignments/{id}/check-in` returns (CUST-ARRIVAL-01).
class CustomerArrivalStatus {
  final int assignmentId;
  final String assignmentStatus;
  final bool awaitingCustomerConfirmation;
  final double? distanceMeters;
  final DateTime? attemptedAt;

  const CustomerArrivalStatus({
    required this.assignmentId,
    required this.assignmentStatus,
    required this.awaitingCustomerConfirmation,
    this.distanceMeters,
    this.attemptedAt,
  });

  factory CustomerArrivalStatus.fromJson(Map<String, dynamic> json) =>
      CustomerArrivalStatus(
        assignmentId: _int(json['assignmentId']),
        assignmentStatus: json['assignmentStatus'] as String? ?? '',
        awaitingCustomerConfirmation:
            json['awaitingCustomerConfirmation'] as bool? ?? false,
        distanceMeters: (json['distanceMeters'] as num?)?.toDouble(),
        attemptedAt: _date(json['attemptedAt']),
      );
}

/// `OrderSummary` of the history list (booking.md section 2).
class OrderSummary {
  final int orderId;
  final String orderCode;
  final String serviceTier;
  final String scheduledDate;
  final String shiftCode;
  final int requiredWorkers;
  final int totalAmount;
  final String orderStatus;
  final DateTime? createdAt;

  const OrderSummary({
    required this.orderId,
    required this.orderCode,
    required this.serviceTier,
    required this.scheduledDate,
    required this.shiftCode,
    required this.requiredWorkers,
    required this.totalAmount,
    required this.orderStatus,
    required this.createdAt,
  });

  factory OrderSummary.fromJson(Map<String, dynamic> json) => OrderSummary(
    orderId: _int(json['orderId']),
    orderCode: json['orderCode'] as String? ?? '',
    serviceTier: json['serviceTier'] as String? ?? '',
    scheduledDate: json['scheduledDate'] as String? ?? '',
    shiftCode: json['shiftCode'] as String? ?? '',
    requiredWorkers: _int(json['requiredWorkers']),
    totalAmount: _int(json['totalAmount']),
    orderStatus: json['orderStatus'] as String? ?? '',
    createdAt: _date(json['createdAt']),
  );
}

/// One page of GET /api/v1/booking/orders (booking.md 3.4).
class OrderPage {
  final List<OrderSummary> items;
  final int page;
  final int pageSize;
  final int total;

  const OrderPage({
    required this.items,
    required this.page,
    required this.pageSize,
    required this.total,
  });

  factory OrderPage.fromJson(Map<String, dynamic> json) => OrderPage(
    items: (json['items'] as List? ?? const [])
        .whereType<Map<String, dynamic>>()
        .map(OrderSummary.fromJson)
        .toList(),
    page: _int(json['page']),
    pageSize: _int(json['pageSize']),
    total: _int(json['total']),
  );
}

/// One visible assignment of `OrderProgress`. There is no worker phone here (Q17).
class ProgressAssignment {
  final int assignmentId;
  final int assignmentSeq;
  final int workerId;
  final String? workerName;
  final double workerRatingAvg;
  final String assignmentStatus;
  final DateTime? acceptedAt;
  final DateTime? completedAt;

  /// ACCEPT-02: the server's acceptance phase and deadlines (null on an older server or before the work).
  final AcceptanceBlock? acceptance;

  const ProgressAssignment({
    required this.assignmentId,
    required this.assignmentSeq,
    required this.workerId,
    required this.workerName,
    required this.workerRatingAvg,
    required this.assignmentStatus,
    required this.acceptedAt,
    required this.completedAt,
    this.acceptance,
  });

  factory ProgressAssignment.fromJson(Map<String, dynamic> json) =>
      ProgressAssignment(
        assignmentId: _int(json['assignmentId']),
        assignmentSeq: _int(json['assignmentSeq']),
        workerId: _int(json['workerId']),
        workerName: json['workerName'] as String?,
        workerRatingAvg: _double(json['workerRatingAvg']),
        assignmentStatus: json['assignmentStatus'] as String? ?? '',
        acceptedAt: _date(json['acceptedAt']),
        completedAt: _date(json['completedAt']),
        acceptance: AcceptanceBlock.fromJson(json['acceptance']),
      );
}

/// `Extension` ("Làm lần 2", booking.md section 2). Amounts are the server's.
class OrderExtension {
  final int extensionId;
  final int orderId;
  final int workerId;
  final double extraHours;
  final int extraAmount;
  final String extStatus;
  final String workerDecision;
  final DateTime? requestedAt;
  final DateTime? decidedAt;

  const OrderExtension({
    required this.extensionId,
    required this.orderId,
    required this.workerId,
    required this.extraHours,
    required this.extraAmount,
    required this.extStatus,
    required this.workerDecision,
    required this.requestedAt,
    required this.decidedAt,
  });

  factory OrderExtension.fromJson(Map<String, dynamic> json) => OrderExtension(
    extensionId: _int(json['extensionId']),
    orderId: _int(json['orderId']),
    workerId: _int(json['workerId']),
    extraHours: _double(json['extraHours']),
    extraAmount: _int(json['extraAmount']),
    extStatus: json['extStatus'] as String? ?? '',
    workerDecision: json['workerDecision'] as String? ?? '',
    requestedAt: _date(json['requestedAt']),
    decidedAt: _date(json['decidedAt']),
  );
}

/// The 2nd job of a large ECONOMY house (decision D35): extra hours right after the shift, no extra charge. `STANDBY` (not opened yet), `OPEN`
/// (opened) or `RELEASED` (the work ended in the first job). The window runs from the shift end.
class OrderContinuation {
  static const standby = 'STANDBY';
  static const open = 'OPEN';
  static const released = 'RELEASED';

  final String status;
  final String? shiftCode;
  final DateTime? windowStartAt;
  final DateTime? windowEndAt;
  final DateTime? openedAt;

  const OrderContinuation({
    required this.status,
    this.shiftCode,
    this.windowStartAt,
    this.windowEndAt,
    this.openedAt,
  });

  static OrderContinuation? fromJson(Object? json) =>
      json is Map<String, dynamic>
      ? OrderContinuation(
          status: json['status'] as String? ?? '',
          shiftCode: json['shiftCode'] as String?,
          windowStartAt: _date(json['windowStartAt']),
          windowEndAt: _date(json['windowEndAt']),
          openedAt: _date(json['openedAt']),
        )
      : null;
}

/// `OrderProgress` (booking.md 3.6).
class OrderProgress {
  final int orderId;
  final String orderStatus;
  final int requiredWorkers;
  final List<ProgressAssignment> assignments;
  final OrderExtension? extension;

  /// D35: null unless the order is a large ECONOMY house.
  final OrderContinuation? continuation;

  const OrderProgress({
    required this.orderId,
    required this.orderStatus,
    required this.requiredWorkers,
    required this.assignments,
    required this.extension,
    this.continuation,
  });

  factory OrderProgress.fromJson(Map<String, dynamic> json) => OrderProgress(
    orderId: _int(json['orderId']),
    orderStatus: json['orderStatus'] as String? ?? '',
    requiredWorkers: _int(json['requiredWorkers']),
    assignments: (json['assignments'] as List? ?? const [])
        .whereType<Map<String, dynamic>>()
        .map(ProgressAssignment.fromJson)
        .toList(),
    extension: json['extension'] is Map<String, dynamic>
        ? OrderExtension.fromJson(json['extension'] as Map<String, dynamic>)
        : null,
    continuation: OrderContinuation.fromJson(json['continuation']),
  );
}

/// What opens the order screen.
class OrderDetailArgs {
  final int orderId;

  const OrderDetailArgs({required this.orderId});
}
