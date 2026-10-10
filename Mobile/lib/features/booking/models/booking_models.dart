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
