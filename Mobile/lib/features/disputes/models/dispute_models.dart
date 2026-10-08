// Models of filing and following a dispute (contract .spec/contracts/disputes.md section 1 and 2.1; open questions D1, D3-D5).

/// Who files: the endpoint prefix follows the role of the signed-in person.
enum DisputeRole { customer, worker }

extension DisputeRoleX on DisputeRole {
  String get pathPrefix => this == DisputeRole.customer ? '/api/customers/me' : '/api/workers/me';
}

/// Allowed `category` values (D1). `ABSENT_FEE` is the customer disputing a customer-absent fee (Q10), so only a customer sees it.
class DisputeCategory {
  final String code;
  final String label;

  const DisputeCategory(this.code, this.label);

  static const quality = DisputeCategory('QUALITY', 'Chất lượng kém');
  static const attitude = DisputeCategory('ATTITUDE', 'Thái độ');
  static const propertyDamage = DisputeCategory('PROPERTY_DAMAGE', 'Hư hỏng tài sản');
  static const absentFee = DisputeCategory('ABSENT_FEE', 'Phí vắng mặt');
  static const other = DisputeCategory('OTHER', 'Khác');
}

/// `Dispute` of the contract.
class Dispute {
  static const open = 'OPEN';
  static const inReview = 'IN_REVIEW';
  static const resolved = 'RESOLVED';
  static const dismissed = 'DISMISSED';

  final int disputeId;
  final int orderId;
  final String raisedBy;
  final String category;
  final String description;
  final List<String> evidenceUrls;
  final String disputeStatus;

  /// FREELANCER, AGENCY or CUSTOMER once resolved; null while open and after a dismissal.
  final String? faultParty;
  final int? compensationAmount;
  final DateTime? slaDueAt;
  final DateTime? resolvedAt;
  final DateTime? createdAt;

  const Dispute({
    required this.disputeId,
    required this.orderId,
    required this.raisedBy,
    required this.category,
    required this.description,
    required this.evidenceUrls,
    required this.disputeStatus,
    this.faultParty,
    this.compensationAmount,
    this.slaDueAt,
    this.resolvedAt,
    this.createdAt,
  });

  factory Dispute.fromJson(Map<String, dynamic> json) => Dispute(
        disputeId: (json['disputeId'] as num?)?.toInt() ?? 0,
        orderId: (json['orderId'] as num?)?.toInt() ?? 0,
        raisedBy: json['raisedBy'] as String? ?? '',
        category: json['category'] as String? ?? '',
        description: json['description'] as String? ?? '',
        evidenceUrls: ((json['evidenceUrls'] as List?) ?? const []).whereType<String>().toList(growable: false),
        disputeStatus: json['disputeStatus'] as String? ?? '',
        faultParty: json['faultParty'] as String?,
        compensationAmount: (json['compensationAmount'] as num?)?.toInt(),
        slaDueAt: DateTime.tryParse(json['slaDueAt'] as String? ?? ''),
        resolvedAt: DateTime.tryParse(json['resolvedAt'] as String? ?? ''),
        createdAt: DateTime.tryParse(json['createdAt'] as String? ?? ''),
      );
}

/// What the person typed, ready to be checked and sent.
class DisputeSubmission {
  final int orderId;
  final String category;
  final String description;
  final List<String> evidenceUrls;

  const DisputeSubmission({
    required this.orderId,
    required this.category,
    required this.description,
    required this.evidenceUrls,
  });

  Map<String, dynamic> toJson() => {
        'orderId': orderId,
        'category': category,
        'description': description,
        'evidenceUrls': evidenceUrls,
      };
}
