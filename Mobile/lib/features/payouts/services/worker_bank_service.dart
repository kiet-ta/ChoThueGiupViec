import '../../../core/network/api_client.dart';

/// The worker's payout account (`GET` and `PUT /api/v1/workers/me/bank`, contract workers.md; decision D19).
/// The strings are empty while the worker never set an account.
class WorkerBank {
  final String bankName;
  final String bankAccountNo;
  final bool isSet;

  const WorkerBank({
    required this.bankName,
    required this.bankAccountNo,
    required this.isSet,
  });

  factory WorkerBank.fromJson(Map<String, dynamic> json) => WorkerBank(
    bankName: json['bankName'] as String? ?? '',
    bankAccountNo: json['bankAccountNo'] as String? ?? '',
    isSet: json['isSet'] as bool? ?? false,
  );
}

/// What the bank account screen needs from the server; a test or a preview can supply its own.
abstract class IWorkerBankService {
  Future<WorkerBank> getBank();

  Future<WorkerBank> saveBank({
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
}
