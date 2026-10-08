import '../../../core/network/api_client.dart';
import '../models/payout_models.dart';

/// What the income screens need from the server; a test or a preview can supply its own.
abstract class IPayoutsService {
  Future<Earnings> getEarnings(String month);

  Future<PayoutHistoryPage> getPayouts({int page = 1, int pageSize = 20});
}

/// The two endpoints of payouts.md 2.5 over the shared [ApiClient]. Failures are thrown as [ApiException].
class PayoutsService implements IPayoutsService {
  final ApiClient _client;

  PayoutsService({ApiClient? client}) : _client = client ?? ApiClient();

  @override
  Future<Earnings> getEarnings(String month) async {
    final response = await _client.get<Earnings>(
      '/api/workers/me/earnings',
      queryParams: {'month': month},
      fromJson: (json) => Earnings.fromJson(json as Map<String, dynamic>),
    );
    final data = response.data;
    if (data == null) throw const ApiException(0, 'Máy chủ không trả dữ liệu thu nhập.');
    return data;
  }

  @override
  Future<PayoutHistoryPage> getPayouts({int page = 1, int pageSize = 20}) async {
    final response = await _client.get<PayoutHistoryPage>(
      '/api/workers/me/payouts',
      queryParams: {'page': '$page', 'pageSize': '$pageSize'},
      fromJson: (json) => PayoutHistoryPage.fromJson(json as Map<String, dynamic>),
    );
    final data = response.data;
    if (data == null) throw const ApiException(0, 'Máy chủ không trả lịch sử giải ngân.');
    return data;
  }
}
