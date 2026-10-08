import '../../../core/network/api_client.dart';
import '../models/rating_models.dart';

/// What the rating screen needs from the server; a test or a preview can supply its own.
abstract class IRatingsService {
  Future<RatingWindow> getWindow(RatingRole role, int assignmentId);

  Future<void> submit(RatingRole role, int assignmentId, RatingSubmission submission);
}

/// The two endpoint pairs of the contract (ratings.md 2.1 and 2.2) over the shared [ApiClient].
/// Failures are thrown as [ApiException], never hidden.
class RatingsService implements IRatingsService {
  final ApiClient _client;

  RatingsService({ApiClient? client}) : _client = client ?? ApiClient();

  String _base(RatingRole role, int assignmentId) => '${role.pathPrefix}/assignments/$assignmentId';

  @override
  Future<RatingWindow> getWindow(RatingRole role, int assignmentId) async {
    final response = await _client.get<RatingWindow>(
      '${_base(role, assignmentId)}/rating-window',
      fromJson: (json) => RatingWindow.fromJson(json as Map<String, dynamic>),
    );
    final data = response.data;
    if (data == null) throw const ApiException(0, 'Máy chủ không trả dữ liệu đánh giá.');
    return data;
  }

  @override
  Future<void> submit(RatingRole role, int assignmentId, RatingSubmission submission) async {
    await _client.post<Object?>('${_base(role, assignmentId)}/rating', body: submission.toJson());
  }
}
