import '../../../core/network/api_client.dart';
import '../../../core/network/api_response.dart';
import '../models/ekyc_models.dart';

/// Service for handling Worker eKYC network requests.
class WorkersEkycService {
  final ApiClient _apiClient;

  WorkersEkycService({ApiClient? apiClient})
      : _apiClient = apiClient ?? ApiClient();

  /// Submits front/back CCCD and selfie photo URLs for automated eKYC verification.
  Future<ApiResponse<EkycResultResponse>> submitEkyc(SubmitEkycRequest request) {
    return _apiClient.post<EkycResultResponse>(
      '/api/workers/me/ekyc',
      body: request.toJson(),
      fromJson: (json) => EkycResultResponse.fromJson(json as Map<String, dynamic>),
    );
  }
}
