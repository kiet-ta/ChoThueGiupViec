import '../../../core/network/api_client.dart';

DateTime? _parseTime(Object? value) =>
    value is String ? DateTime.tryParse(value) : null;

/// A request to change the account that waits for the Admin (`pendingRequest` of the bank answer).
class BankChangePending {
  final int requestId;
  final String bankName;
  final String bankAccountNo;
  final DateTime? requestedAt;

  const BankChangePending({
    required this.requestId,
    required this.bankName,
    required this.bankAccountNo,
    this.requestedAt,
  });

  factory BankChangePending.fromJson(Map<String, dynamic> json) =>
      BankChangePending(
        requestId: (json['requestId'] as num?)?.toInt() ?? 0,
        bankName: json['bankName'] as String? ?? '',
        bankAccountNo: json['bankAccountNo'] as String? ?? '',
        requestedAt: _parseTime(json['requestedAt']),
      );
}

/// The latest request of the worker, whatever its state (`lastRequest` of the bank answer).
class BankChangeLast {
  /// PENDING, APPROVED or REJECTED.
  final String status;
  final String? rejectReason;
  final DateTime? requestedAt;
  final DateTime? reviewedAt;

  const BankChangeLast({
    required this.status,
    this.rejectReason,
    this.requestedAt,
    this.reviewedAt,
  });

  bool get isRejected => status == 'REJECTED';
  bool get isApproved => status == 'APPROVED';

  factory BankChangeLast.fromJson(Map<String, dynamic> json) => BankChangeLast(
    status: json['status'] as String? ?? '',
    rejectReason: json['rejectReason'] as String?,
    requestedAt: _parseTime(json['requestedAt']),
    reviewedAt: _parseTime(json['reviewedAt']),
  );
}

/// The worker's payout account and what the worker can do about it (`GET /api/v1/workers/me/bank`, contract workers.md, LEDGER-28):
/// the first account is saved at once, a change is a request an Admin approves, and 15 days after a request was sent no new one can be sent.
/// The strings are empty while the worker never set an account.
class WorkerBank {
  final String bankName;
  final String bankAccountNo;
  final bool isSet;

  /// True only with an account, no pending request and no lock.
  final bool canRequestChange;

  /// When the lock ends (the day the latest request was sent plus 15 days); null when there is no lock.
  final DateTime? changeLockedUntil;
  final BankChangePending? pendingRequest;
  final BankChangeLast? lastRequest;

  const WorkerBank({
    required this.bankName,
    required this.bankAccountNo,
    required this.isSet,
    this.canRequestChange = false,
    this.changeLockedUntil,
    this.pendingRequest,
    this.lastRequest,
  });

  factory WorkerBank.fromJson(Map<String, dynamic> json) {
    final pending = json['pendingRequest'];
    final last = json['lastRequest'];
    return WorkerBank(
      bankName: json['bankName'] as String? ?? '',
      bankAccountNo: json['bankAccountNo'] as String? ?? '',
      isSet: json['isSet'] as bool? ?? false,
      canRequestChange: json['canRequestChange'] as bool? ?? false,
      changeLockedUntil: _parseTime(json['changeLockedUntil']),
      pendingRequest: pending is Map<String, dynamic>
          ? BankChangePending.fromJson(pending)
          : null,
      lastRequest: last is Map<String, dynamic>
          ? BankChangeLast.fromJson(last)
          : null,
    );
  }
}

/// What the bank account screen needs from the server; a test or a preview can supply its own.
abstract class IWorkerBankService {
  Future<WorkerBank> getBank();

  /// Sets the FIRST account (`PUT`): no approval. The server answers 409 `BANK_CHANGE_REQUIRES_REQUEST` when there already is one.
  Future<WorkerBank> saveBank({
    required String bankName,
    required String bankAccountNo,
  });

  /// Sends a request to change the account (`POST /api/v1/workers/me/bank-change-requests`); an Admin approves it.
  Future<WorkerBank> requestChange({
    required String bankName,
    required String bankAccountNo,
  });
}

/// The two endpoints over the shared [ApiClient]. Failures are thrown as [ApiException].
class WorkerBankService implements IWorkerBankService {
  final ApiClient _client;

  WorkerBankService({ApiClient? client}) : _client = client ?? ApiClient();

  @override
  Future<WorkerBank> getBank() async {
    final response = await _client.get<WorkerBank>(
      '/api/v1/workers/me/bank',
      fromJson: (json) => WorkerBank.fromJson(json as Map<String, dynamic>),
    );
    final data = response.data;
    if (data == null) {
      throw const ApiException(0, 'Máy chủ không trả tài khoản nhận tiền.');
    }
    return data;
  }

  @override
  Future<WorkerBank> saveBank({
    required String bankName,
    required String bankAccountNo,
  }) async {
    final response = await _client.put<WorkerBank>(
      '/api/v1/workers/me/bank',
      body: {'bankName': bankName, 'bankAccountNo': bankAccountNo},
      fromJson: (json) => WorkerBank.fromJson(json as Map<String, dynamic>),
    );
    final data = response.data;
    if (data == null) {
      throw const ApiException(0, 'Máy chủ không trả tài khoản nhận tiền.');
    }
    return data;
  }

  @override
  Future<WorkerBank> requestChange({
    required String bankName,
    required String bankAccountNo,
  }) async {
    final response = await _client.post<WorkerBank>(
      '/api/v1/workers/me/bank-change-requests',
      body: {'bankName': bankName, 'bankAccountNo': bankAccountNo},
      fromJson: (json) => WorkerBank.fromJson(json as Map<String, dynamic>),
    );
    final data = response.data;
    if (data == null) {
      throw const ApiException(0, 'Máy chủ không trả tài khoản nhận tiền.');
    }
    return data;
  }
}
