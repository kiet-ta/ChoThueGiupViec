// ACCEPT-02 (decision D32): the 24-hour acceptance window as both sides show it. Every time comes from the server's `acceptance` block
// (contract workers.md 2.5.4); the device never counts the window itself, it only compares "now" with the server's deadlines to pick a
// sentence. Customer copy says "người làm", worker copy says "bạn".

/// The phases of the server's acceptance block.
class AcceptancePhase {
  static const inProgress = 'IN_PROGRESS';
  static const onSite = 'ON_SITE';
  static const review = 'REVIEW';
  static const disputed = 'DISPUTED';
  static const completed = 'COMPLETED';

  static const known = {inProgress, onSite, review, disputed, completed};
}

/// Who accepted a completed job.
class CompletionSourceValue {
  static const customer = 'CUSTOMER';
  static const auto = 'AUTO';
  static const verdict = 'VERDICT';
}

/// The `acceptance` block of an assignment (worker job detail, customer order progress).
class AcceptanceBlock {
  final String phase;
  final DateTime? submittedAt;
  final DateTime? redoUntil;
  final DateTime? autoAcceptAt;
  final DateTime? disputeUntil;
  final String? completionSource;
  final int redoCount;

  const AcceptanceBlock({
    required this.phase,
    this.submittedAt,
    this.redoUntil,
    this.autoAcceptAt,
    this.disputeUntil,
    this.completionSource,
    this.redoCount = 0,
  });

  static AcceptanceBlock? fromJson(Object? json) {
    if (json is! Map<String, dynamic>) return null;
    DateTime? date(String key) => DateTime.tryParse(json[key] as String? ?? '');
    return AcceptanceBlock(
      phase: json['phase'] as String? ?? '',
      submittedAt: date('submittedAt'),
      redoUntil: date('redoUntil'),
      autoAcceptAt: date('autoAcceptAt'),
      disputeUntil: date('disputeUntil'),
      completionSource: json['completionSource'] as String?,
      redoCount: (json['redoCount'] as num?)?.toInt() ?? 0,
    );
  }
}

/// What one side reads about a job: a short status and the sentence under it.
class AcceptanceView {
  final String title;
  final String note;

  const AcceptanceView(this.title, this.note);
}

const String unknownAcceptanceTitle = 'Đang cập nhật trạng thái';

/// `HH:mm` in Asia/Ho_Chi_Minh (UTC+7), with `dd/MM` when the day is not today's (decision G-3).
String acceptanceTime(DateTime instant, DateTime now) {
  final local = instant.toUtc().add(const Duration(hours: 7));
  final today = now.toUtc().add(const Duration(hours: 7));
  String two(int v) => v.toString().padLeft(2, '0');
  final time = '${two(local.hour)}:${two(local.minute)}';
  final sameDay =
      local.year == today.year &&
      local.month == today.month &&
      local.day == today.day;
  return sameDay ? time : '$time ${two(local.day)}/${two(local.month)}';
}

/// The redo can be asked only while the worker is still on site (before `redoUntil`).
bool canAskRedo(AcceptanceBlock? block, DateTime now) {
  if (block == null) return true; // an older server without the block: the server stays the judge
  final until = block.redoUntil;
  return block.phase != AcceptancePhase.completed &&
      until != null &&
      now.isBefore(until);
}

/// Whether the phase is one the apps know; anything else is shown as "Đang cập nhật trạng thái" and reloaded.
bool isKnownPhase(AcceptanceBlock block) =>
    AcceptancePhase.known.contains(block.phase);

String _completedNote(
  AcceptanceBlock b,
  DateTime now, {
  required bool customer,
}) => switch (b.completionSource) {
  CompletionSourceValue.auto => 'Đã tự nghiệm thu sau 24 giờ.',
  CompletionSourceValue.verdict => 'Đã đóng theo kết quả khiếu nại.',
  _ => customer ? 'Bạn đã nghiệm thu ca này.' : 'Khách đã nghiệm thu.',
};

/// The customer's view of a job in the window; null when the block says nothing to show (no block, or before the work).
AcceptanceView? customerAcceptanceView(AcceptanceBlock? b, DateTime now) {
  if (b == null) return null;
  if (!isKnownPhase(b)) {
    return const AcceptanceView(unknownAcceptanceTitle, 'Màn hình sẽ tải lại.');
  }
  return switch (b.phase) {
    AcceptancePhase.onSite => AcceptanceView(
      'Mời bạn kiểm tra',
      b.redoUntil == null
          ? 'Người làm vẫn đang ở nhà bạn.'
          : 'Người làm còn ở đây đến ${acceptanceTime(b.redoUntil!, now)}. Chỗ nào chưa sạch, hãy nhờ dọn lại ngay.',
    ),
    AcceptancePhase.review => AcceptanceView(
      'Chờ bạn nghiệm thu',
      b.autoAcceptAt == null
          ? 'Hãy kiểm tra và nghiệm thu ca làm.'
          : 'Tự nghiệm thu lúc ${acceptanceTime(b.autoAcceptAt!, now)} nếu bạn không phản hồi.',
    ),
    AcceptancePhase.disputed => const AcceptanceView(
      'Đang xử lý khiếu nại',
      'Admin đang xem khiếu nại. Bạn vẫn có thể nghiệm thu nếu đã hài lòng.',
    ),
    AcceptancePhase.completed => AcceptanceView(
      'Hoàn tất',
      _completedNote(b, now, customer: true),
    ),
    _ => null,
  };
}

/// The worker's view of a submitted job; null before the photos are submitted.
AcceptanceView? workerAcceptanceView(AcceptanceBlock? b, DateTime now) {
  if (b == null) return null;
  if (!isKnownPhase(b)) {
    return const AcceptanceView(unknownAcceptanceTitle, 'Màn hình sẽ tải lại.');
  }
  return switch (b.phase) {
    AcceptancePhase.onSite => AcceptanceView(
      'Khách đang kiểm tra',
      b.redoUntil == null
          ? 'Hãy ở lại để khách kiểm tra.'
          : 'Khách có thể nhờ dọn lại đến ${acceptanceTime(b.redoUntil!, now)}. Hãy ở lại đến lúc đó.',
    ),
    AcceptancePhase.review => AcceptanceView(
      'Đã bàn giao',
      b.autoAcceptAt == null
          ? 'Bạn có thể rời đi. Tiền công ghi nhận khi khách nghiệm thu.'
          : 'Bạn có thể rời đi. Tiền công ghi nhận khi khách nghiệm thu, chậm nhất ${acceptanceTime(b.autoAcceptAt!, now)}.',
    ),
    AcceptancePhase.disputed => const AcceptanceView(
      'Ca đang có khiếu nại',
      'Admin đang xem. Tiền công chờ kết quả.',
    ),
    AcceptancePhase.completed => AcceptanceView(
      'Hoàn tất',
      _completedNote(b, now, customer: false),
    ),
    _ => null,
  };
}

/// What a refused redo tells the customer (409 `REDO_WINDOW_CLOSED`).
const String redoWindowClosedText =
    'Người làm đã rời đi nên không nhờ dọn lại được nữa. Bạn có thể nghiệm thu hoặc gửi khiếu nại.';
