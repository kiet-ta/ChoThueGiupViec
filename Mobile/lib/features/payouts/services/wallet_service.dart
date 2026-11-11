import '../../../core/network/api_client.dart';
import '../models/wallet_models.dart';

/// What the wallet screen needs from the server; a test or a preview can supply its own.
abstract class IWalletService {
  Future<Wallet> getWallet();

  Future<Withdrawal> requestWithdrawal(int amount);

  Future<Withdrawal> cancelWithdrawal(int withdrawalId);
}

/// The wallet endpoints of ledger.md section 6 over the shared [ApiClient]. Failures are thrown as [ApiException].
class WalletService implements IWalletService {
  final ApiClient _client;

  WalletService({ApiClient? client}) : _client = client ?? ApiClient();

  @override
  Future<Wallet> getWallet() async {
    final response = await _client.get<Wallet>(
      '/api/v1/workers/me/wallet',
      fromJson: (json) => Wallet.fromJson(json as Map<String, dynamic>),
    );
    final data = response.data;
    if (data == null) {
      throw const ApiException(0, 'Máy chủ không trả dữ liệu số dư.');
    }
    return data;
  }

  @override
  Future<Withdrawal> requestWithdrawal(int amount) async {
    final response = await _client.post<Withdrawal>(
      '/api/v1/workers/me/withdrawals',
      body: {'amount': amount},
      fromJson: (json) => Withdrawal.fromJson(json as Map<String, dynamic>),
    );
    final data = response.data;
    if (data == null) {
      throw const ApiException(0, 'Máy chủ không trả yêu cầu rút tiền.');
    }
    return data;
  }

  @override
  Future<Withdrawal> cancelWithdrawal(int withdrawalId) async {
    final response = await _client.post<Withdrawal>(
      '/api/v1/workers/me/withdrawals/$withdrawalId/cancel',
      fromJson: (json) => Withdrawal.fromJson(json as Map<String, dynamic>),
    );
    final data = response.data;
    if (data == null) {
      throw const ApiException(0, 'Máy chủ không trả yêu cầu rút tiền.');
    }
    return data;
  }
}
