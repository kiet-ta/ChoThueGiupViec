import '../../../core/network/api_client.dart';
import '../models/wallet_models.dart';

/// A page of the worker's own withdrawal requests, newest first (`GET /api/v1/workers/me/withdrawals`).
class WithdrawalPage {
  final List<Withdrawal> items;
  final int page;
  final int pageSize;
  final int total;

  const WithdrawalPage({
    required this.items,
    required this.page,
    required this.pageSize,
    required this.total,
  });

  factory WithdrawalPage.fromJson(Map<String, dynamic> json) => WithdrawalPage(
    items: ((json['items'] as List?) ?? const [])
        .whereType<Map<String, dynamic>>()
        .map(Withdrawal.fromJson)
        .toList(growable: false),
    page: (json['page'] as num?)?.toInt() ?? 1,
    pageSize: (json['pageSize'] as num?)?.toInt() ?? 0,
    total: (json['total'] as num?)?.toInt() ?? 0,
  );
}

/// What the withdrawal history screen needs from the server; a test or a preview can supply its own.
abstract class IWithdrawalHistoryService {
  Future<WithdrawalPage> getWithdrawals({int page = 1, int pageSize = 20});
}

/// The list endpoint of ledger.md section 6 over the shared [ApiClient]. Failures are thrown as [ApiException].
class WithdrawalHistoryService implements IWithdrawalHistoryService {
  final ApiClient _client;

  WithdrawalHistoryService({ApiClient? client})
    : _client = client ?? ApiClient();

  @override
  Future<WithdrawalPage> getWithdrawals({
    int page = 1,
    int pageSize = 20,
  }) async {
    final response = await _client.get<WithdrawalPage>(
      '/api/v1/workers/me/withdrawals',
      queryParams: {'page': '$page', 'pageSize': '$pageSize'},
      fromJson: (json) => WithdrawalPage.fromJson(json as Map<String, dynamic>),
    );
    final data = response.data;
    if (data == null) {
      throw const ApiException(0, 'Máy chủ không trả lịch sử rút tiền.');
    }
    return data;
  }
}
