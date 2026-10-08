// Models of the two-way rating (contract .spec/contracts/ratings.md sections 1, 2.1, 2.2; decision Q14).

/// Who is rating: the customer rates the worker, the worker rates the customer.
enum RatingRole { customer, worker }

/// One fixed criterion of a role. [key] is the camelCase key of the API, [label] what the person reads.
class RatingCriterion {
  final String key;
  final String label;

  const RatingCriterion(this.key, this.label);
}

extension RatingRoleX on RatingRole {
  /// `customers/me` or `workers/me`: the policy of the endpoint follows the role of the signed-in person.
  String get pathPrefix => this == RatingRole.customer ? '/api/customers/me' : '/api/workers/me';

  /// The criteria are fixed by decision Q14: the server rejects any other key and any missing key.
  List<RatingCriterion> get criteria => this == RatingRole.customer
      ? const [
          RatingCriterion('punctuality', 'Đúng giờ'),
          RatingCriterion('cleaningQuality', 'Chất lượng dọn dẹp'),
          RatingCriterion('attitude', 'Thái độ'),
        ]
      : const [
          RatingCriterion('cooperation', 'Phối hợp của gia chủ'),
          RatingCriterion('workingConditions', 'Điều kiện làm việc'),
        ];

  /// A worker's rating of a customer is internal only (Q14): the customer never sees it.
  bool get isInternalOnly => this == RatingRole.worker;

  String get title => this == RatingRole.customer ? 'Đánh giá thợ' : 'Đánh giá gia chủ';
}

/// `RatingWindow` of the contract: whether the assignment can be rated now and why not.
class RatingWindow {
  static const open = 'OPEN';
  static const notCompleted = 'NOT_COMPLETED';
  static const windowClosed = 'WINDOW_CLOSED';
  static const alreadyRated = 'ALREADY_RATED';

  final int assignmentId;
  final bool canRate;

  /// OPEN, NOT_COMPLETED, WINDOW_CLOSED or ALREADY_RATED.
  final String reason;
  final DateTime? opensAt;
  final DateTime? closesAt;

  /// Whole seconds until the window closes; 0 when it is not open.
  final int secondsRemaining;

  const RatingWindow({
    required this.assignmentId,
    required this.canRate,
    required this.reason,
    this.opensAt,
    this.closesAt,
    this.secondsRemaining = 0,
  });

  factory RatingWindow.fromJson(Map<String, dynamic> json) => RatingWindow(
        assignmentId: (json['assignmentId'] as num?)?.toInt() ?? 0,
        canRate: json['canRate'] as bool? ?? false,
        reason: json['reason'] as String? ?? '',
        opensAt: DateTime.tryParse(json['opensAt'] as String? ?? ''),
        closesAt: DateTime.tryParse(json['closesAt'] as String? ?? ''),
        secondsRemaining: (json['secondsRemaining'] as num?)?.toInt() ?? 0,
      );
}

/// What the person typed, ready to be checked and sent.
class RatingSubmission {
  final int stars;
  final Map<String, int> criteria;
  final String? comment;

  const RatingSubmission({required this.stars, required this.criteria, this.comment});

  Map<String, dynamic> toJson() => {
        'stars': stars,
        'criteria': criteria,
        'comment': comment,
      };
}
