// Models of the customer payment screen, hand-written from .spec/contracts/payments.md (1.1, 1.3, 2.1 to 2.3) until gate G4.

/// `txnStatus` values of the contract (payments.md 1.1).
class PaymentTxnStatus {
  static const pending = 'PENDING';
  static const success = 'SUCCESS';
  static const expired = 'EXPIRED';
  static const refunded = 'REFUNDED';

  PaymentTxnStatus._();
}

/// `Payment` (payments.md 1.3). The gateway reference and the IPN payload are never sent by the server.
class Payment {
  final int paymentId;

  /// ORDER or EXTENSION.
  final String purpose;
  final int? orderId;
  final int? extensionId;

  /// MOMO or FAKE.
  final String gateway;
  final int amount;
  final String txnStatus;

  /// The text a QR would encode; only while PENDING.
  final String? qrPayload;

  /// Opens the gateway's sandbox page; only returned when the QR is created.
  final String? payUrl;
  final DateTime? expiresAt;
  final DateTime? paidAt;
  final DateTime? createdAt;

  /// Always true in the MVP: sandbox only, no real money (decisions G-1, Q04).
  final bool sandbox;

  const Payment({
    required this.paymentId,
    required this.purpose,
    required this.orderId,
    required this.extensionId,
    required this.gateway,
    required this.amount,
    required this.txnStatus,
    required this.qrPayload,
    required this.payUrl,
    required this.expiresAt,
    required this.paidAt,
    required this.createdAt,
    required this.sandbox,
  });

  static DateTime? _date(dynamic v) => v is String ? DateTime.tryParse(v)?.toUtc() : null;

  factory Payment.fromJson(Map<String, dynamic> json) => Payment(
        paymentId: (json['paymentId'] as num?)?.toInt() ?? 0,
        purpose: json['purpose'] as String? ?? '',
        orderId: (json['orderId'] as num?)?.toInt(),
        extensionId: (json['extensionId'] as num?)?.toInt(),
        gateway: json['gateway'] as String? ?? '',
        amount: (json['amount'] as num?)?.toInt() ?? 0,
        txnStatus: json['txnStatus'] as String? ?? '',
        qrPayload: json['qrPayload'] as String?,
        payUrl: json['payUrl'] as String?,
        expiresAt: _date(json['expiresAt']),
        paidAt: _date(json['paidAt']),
        createdAt: _date(json['createdAt']),
        sandbox: json['sandbox'] as bool? ?? true,
      );

  bool get isPending => txnStatus == PaymentTxnStatus.pending;
  bool get isPaid => txnStatus == PaymentTxnStatus.success;
}

/// What opens the payment screen: the order to pay, or the extension ("Làm lần 2") of an order.
class PaymentScreenArgs {
  final int orderId;

  /// Set when the payment is for an extension of [orderId] instead of the order itself.
  final int? extensionId;

  /// Shown in the title while the QR loads; optional.
  final String? orderCode;

  const PaymentScreenArgs({required this.orderId, this.extensionId, this.orderCode});
}
