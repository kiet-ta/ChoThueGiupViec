import '../../../core/network/api_client.dart';
import '../models/dispute_models.dart';

/// What the dispute screens need from the server; a test or a preview can supply its own.
abstract class IDisputesService {
  Future<Dispute> create(DisputeRole role, DisputeSubmission submission);

  Future<List<Dispute>> listMine(DisputeRole role);
}

/// The endpoints of disputes.md 2.1 over the shared [ApiClient]. Failures are thrown as [ApiException].
class DisputesService implements IDisputesService {
  final ApiClient _client;

  DisputesService({ApiClient? client}) : _client = client ?? ApiClient();

  @override
  Future<Dispute> create(DisputeRole role, DisputeSubmission submission) async {
    final response = await _client.post<Dispute>(
      '${role.pathPrefix}/disputes',
      body: submission.toJson(),
      fromJson: (json) => Dispute.fromJson(json as Map<String, dynamic>),
    );
    final data = response.data;
    if (data == null) throw const ApiException(0, 'Máy chủ không trả khiếu nại vừa tạo.');
    return data;
  }

  @override
  Future<List<Dispute>> listMine(DisputeRole role) async {
    final response = await _client.get<List<Dispute>>(
      '${role.pathPrefix}/disputes',
      fromJson: (json) => ((json as List?) ?? const []).whereType<Map<String, dynamic>>().map(Dispute.fromJson).toList(growable: false),
    );
    return response.data ?? const [];
  }
}
