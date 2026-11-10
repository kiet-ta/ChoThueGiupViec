import '../../../core/network/api_client.dart';
import '../models/ledger_models.dart';

/// What the history and statement screens need from the server; a test or a preview can supply its own.
abstract class ILedgerService {
  /// [from] and [to] are Vietnam days (`yyyy-MM-dd`), [type] an entry type; any of them may be null.
  Future<LedgerHistoryPage> getLedger({
    String? from,
    String? to,
    String? type,
    int page = 1,
    int pageSize = 20,
  });

  Future<WorkerStatement> getStatement(String month);
}

/// The history endpoints of ledger.md sections 5 and 9 over the shared [ApiClient]. Failures are thrown as [ApiException].
class LedgerService implements ILedgerService {
  final ApiClient _client;

  LedgerService({ApiClient? client}) : _client = client ?? ApiClient();

  @override
  Future<LedgerHistoryPage> getLedger({
    String? from,
    String? to,
    String? type,
    int page = 1,
    int pageSize = 20,
  }) async {
    final response = await _client.get<LedgerHistoryPage>(
      '/api/v1/workers/me/ledger',
      queryParams: {
        'from': ?from,
        'to': ?to,
        'type': ?type,
        'page': '$page',
        'pageSize': '$pageSize',
      },
      fromJson: (json) =>
          LedgerHistoryPage.fromJson(json as Map<String, dynamic>),
    );
    final data = response.data;
    if (data == null) {
      throw const ApiException(0, 'Máy chủ không trả lịch sử giao dịch.');
    }
    return data;
  }

  @override
  Future<WorkerStatement> getStatement(String month) async {
    final response = await _client.get<WorkerStatement>(
      '/api/v1/workers/me/statements/$month',
      fromJson: (json) =>
          WorkerStatement.fromJson(json as Map<String, dynamic>),
    );
    final data = response.data;
    if (data == null) {
      throw const ApiException(0, 'Máy chủ không trả sao kê tháng.');
    }
    return data;
  }
}
