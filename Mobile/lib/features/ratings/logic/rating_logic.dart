import '../../../core/network/api_client.dart';
import '../models/rating_models.dart';

/// Pure rules of the rating screen (no Flutter, no network), written from the contract and decision Q14.

const int minScore = 1;
const int maxScore = 5;
const int maxCommentLength = 500; // TWO_WAY_RATING.comment NVARCHAR(500)

/// "Còn 47 giờ 12 phút": the time left of the 48 h window. Under a minute says so; zero or less is over.
String countdownText(int seconds) {
  if (seconds <= 0) return 'Đã hết hạn đánh giá';
  if (seconds < 60) return 'Còn dưới 1 phút';
  final minutes = seconds ~/ 60;
  final hours = minutes ~/ 60;
  final rest = minutes % 60;
  return hours == 0 ? 'Còn $rest phút' : 'Còn $hours giờ $rest phút';
}

/// Why the assignment cannot be rated now, in words. [RatingWindow.open] has no message.
String? reasonMessage(String reason) {
  switch (reason) {
    case RatingWindow.notCompleted:
      return 'Ca làm chưa hoàn thành nên chưa đánh giá được. Bạn sẽ đánh giá được ngay khi ca hoàn tất.';
    case RatingWindow.windowClosed:
      return 'Đã quá 48 giờ kể từ khi ca hoàn thành nên không còn đánh giá được.';
    case RatingWindow.alreadyRated:
      return 'Bạn đã đánh giá ca này rồi. Đánh giá không thể sửa hay gửi lại.';
    default:
      return null;
  }
}

/// Mistakes of a submission, by field. [criteria] is keyed by the API key of the criterion.
class RatingErrors {
  final String? stars;
  final Map<String, String> criteria;
  final String? comment;

  const RatingErrors({this.stars, this.criteria = const {}, this.comment});

  bool get isEmpty => stars == null && criteria.isEmpty && comment == null;
}

bool _inRange(int? value) => value != null && value >= minScore && value <= maxScore;

/// Mirrors the 400 rules of the contract so the person sees the problem before sending: stars 1-5, every fixed criterion
/// present and 1-5, a comment of at most 500 characters once trimmed.
RatingErrors validateRating(RatingRole role, int? stars, Map<String, int> criteria, String comment) {
  final criteriaErrors = <String, String>{};
  for (final c in role.criteria) {
    if (!_inRange(criteria[c.key])) criteriaErrors[c.key] = 'Chọn từ $minScore đến $maxScore sao cho "${c.label}".';
  }
  final trimmed = comment.trim();
  return RatingErrors(
    stars: _inRange(stars) ? null : 'Chọn số sao tổng thể từ $minScore đến $maxScore.',
    criteria: criteriaErrors,
    comment: trimmed.length > maxCommentLength ? 'Nhận xét tối đa $maxCommentLength ký tự (đang ${trimmed.length}).' : null,
  );
}

/// The request body: only the fixed keys of the role, a trimmed comment, `null` when it is blank.
/// Call it only after [validateRating] returned no errors.
RatingSubmission buildSubmission(RatingRole role, int stars, Map<String, int> criteria, String comment) {
  final trimmed = comment.trim();
  return RatingSubmission(
    stars: stars,
    criteria: {for (final c in role.criteria) c.key: criteria[c.key]!},
    comment: trimmed.isEmpty ? null : trimmed,
  );
}

/// A failed call turned into what the person reads. [reloadWindow] is true when the window changed under them (409), so the
/// screen should load it again and show the real reason.
class RatingFailure {
  final String message;
  final Map<String, String> fields;
  final bool reloadWindow;

  const RatingFailure(this.message, {this.fields = const {}, this.reloadWindow = false});
}

RatingFailure ratingFailure(ApiException e) {
  if (e.isNetworkError) return RatingFailure(e.message);
  if (e.isNotFound) return const RatingFailure('Không tìm thấy ca làm này.');
  if (e.isConflict) {
    return const RatingFailure(
      'Không gửi được: ca chưa hoàn thành, đã quá 48 giờ, hoặc bạn đã đánh giá rồi. Đang tải lại trạng thái.',
      reloadWindow: true,
    );
  }
  if (e.statusCode == 400) {
    final fields = <String, String>{};
    e.fieldErrors?.forEach((key, messages) {
      if (messages.isNotEmpty) fields[key] = messages.first;
    });
    return RatingFailure(fields.isEmpty ? e.message : 'Kiểm tra lại các ô báo lỗi.', fields: fields);
  }
  return RatingFailure(e.message);
}
