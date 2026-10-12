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

  OrderStatus._();
}

int _int(dynamic v) => (v as num?)?.toInt() ?? 0;
double _double(dynamic v) => (v as num?)?.toDouble() ?? 0;
DateTime? _date(dynamic v) => v is String ? DateTime.tryParse(v)?.toUtc() : null;

/// One shift of GET /api/booking/options: the code and its local (Asia/Ho_Chi_Minh) start and end, "HH:mm".
class ShiftOption {
  final String shiftCode;
  final String startLocal;
  final String endLocal;

  const ShiftOption({required this.shiftCode, required this.startLocal, required this.endLocal});

  factory ShiftOption.fromJson(Map<String, dynamic> json) => ShiftOption(
        shiftCode: json['shiftCode'] as String? ?? '',
        startLocal: json['startLocal'] as String? ?? '',
        endLocal: json['endLocal'] as String? ?? '',
      );
}

/// GET /api/booking/options (booking.md 3.1): the app hard-codes none of these values.
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
        serviceTiers: (json['serviceTiers'] as List? ?? const []).map((e) => e.toString()).toList(),
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

  const PriceQuote({
    required this.addressId,
    required this.serviceTier,
    required this.totalAreaM2,
    required this.areaBracket,
    required this.requiredWorkers,
    required this.unitPrice,
    required this.totalAmount,
    required this.currency,
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
  final String? requiredSkill;
  final int totalAmount;
  final String orderStatus;
  final String? customerNote;
  final String? cancelReason;
  final DateTime? paymentDeadlineAt;
  final DateTime? createdAt;

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
    required this.requiredSkill,
    required this.totalAmount,
    required this.orderStatus,
    required this.customerNote,
    required this.cancelReason,
    required this.paymentDeadlineAt,
    required this.createdAt,
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
        requiredSkill: json['requiredSkill'] as String?,
        totalAmount: _int(json['totalAmount']),
        orderStatus: json['orderStatus'] as String? ?? '',
        customerNote: json['customerNote'] as String?,
        cancelReason: json['cancelReason'] as String?,
        paymentDeadlineAt: _date(json['paymentDeadlineAt']),
        createdAt: _date(json['createdAt']),
      );
}

/// What the customer chose; the body of POST /api/booking/orders (booking.md 3.3).
class CreateOrderInput {
  final int addressId;
  final String serviceTier;

  /// "yyyy-MM-dd".
  final String scheduledDate;
  final String shiftCode;
  final String? customerNote;
  final String? requiredSkill;

  const CreateOrderInput({
    required this.addressId,
    required this.serviceTier,
    required this.scheduledDate,
    required this.shiftCode,
    this.customerNote,
    this.requiredSkill,
  });

  Map<String, dynamic> toJson() => {
        'addressId': addressId,
        'serviceTier': serviceTier,
        'scheduledDate': scheduledDate,
        'shiftCode': shiftCode,
        'customerNote': customerNote,
        'requiredSkill': requiredSkill,
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

/// One page of GET /api/booking/orders (booking.md 3.4).
class OrderPage {
  final List<OrderSummary> items;
  final int page;
  final int pageSize;
  final int total;

  const OrderPage({required this.items, required this.page, required this.pageSize, required this.total});

  factory OrderPage.fromJson(Map<String, dynamic> json) => OrderPage(
        items: (json['items'] as List? ?? const []).whereType<Map<String, dynamic>>().map(OrderSummary.fromJson).toList(),
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

  const ProgressAssignment({
    required this.assignmentId,
    required this.assignmentSeq,
    required this.workerId,
    required this.workerName,
    required this.workerRatingAvg,
    required this.assignmentStatus,
    required this.acceptedAt,
    required this.completedAt,
  });

  factory ProgressAssignment.fromJson(Map<String, dynamic> json) => ProgressAssignment(
        assignmentId: _int(json['assignmentId']),
        assignmentSeq: _int(json['assignmentSeq']),
        workerId: _int(json['workerId']),
        workerName: json['workerName'] as String?,
        workerRatingAvg: _double(json['workerRatingAvg']),
        assignmentStatus: json['assignmentStatus'] as String? ?? '',
        acceptedAt: _date(json['acceptedAt']),
        completedAt: _date(json['completedAt']),
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

/// `OrderProgress` (booking.md 3.6).
class OrderProgress {
  final int orderId;
  final String orderStatus;
  final int requiredWorkers;
  final List<ProgressAssignment> assignments;
  final OrderExtension? extension;

  const OrderProgress({
    required this.orderId,
    required this.orderStatus,
    required this.requiredWorkers,
    required this.assignments,
    required this.extension,
  });

  factory OrderProgress.fromJson(Map<String, dynamic> json) => OrderProgress(
        orderId: _int(json['orderId']),
        orderStatus: json['orderStatus'] as String? ?? '',
        requiredWorkers: _int(json['requiredWorkers']),
        assignments:
            (json['assignments'] as List? ?? const []).whereType<Map<String, dynamic>>().map(ProgressAssignment.fromJson).toList(),
        extension: json['extension'] is Map<String, dynamic> ? OrderExtension.fromJson(json['extension'] as Map<String, dynamic>) : null,
      );
}

/// What opens the order screen.
class OrderDetailArgs {
  final int orderId;

  const OrderDetailArgs({required this.orderId});
}
