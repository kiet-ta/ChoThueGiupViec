// Pure logic of the order screens (history, tracking, cancel, "Làm lần 2"): labels, what is offered when, error texts by code.
import '../../../core/network/api_client.dart';
import '../models/booking_models.dart';
import 'booking_logic.dart';

/// Business codes of the cancel and extension endpoints (booking.md 3.7, 3.8).
class OrderErrorCode {
  static const invalidState = 'INVALID_STATE';
  static const extensionExists = 'EXTENSION_EXISTS';

  OrderErrorCode._();
}

const int cancelReasonMaxLength = 255;
const int historyPageSize = 20;

/// How often the tracking screen asks the server again (the contract's polling fallback, booking.md 3.6).
const Duration defaultProgressPollInterval = Duration(seconds: 5);

String orderStatusLabel(String status) => switch (status) {
  OrderStatus.pendingPayment => 'Chờ thanh toán',
  OrderStatus.paid => 'Đã thanh toán',
  OrderStatus.dispatching => 'Đang tìm người làm',
  OrderStatus.assigned => 'Đã có người làm',
  OrderStatus.awaitingFallback => 'Chờ bạn chọn: 1 người làm 2 ca',
  OrderStatus.completed => 'Hoàn thành',
  OrderStatus.cancelled => 'Đã huỷ',
  _ => status,
};

/// The progress endpoint can expose an assigned worker before the aggregate order status catches up.
/// Keep the customer-facing tracking state at "assigned" while any worker still holds the job.
String orderStatusForDisplay(
  String status,
  List<ProgressAssignment> assignments,
) {
  if (status != OrderStatus.dispatching) return status;
  const activeWorkerStatuses = {
    AssignmentStatus.assigned,
    AssignmentStatus.checkedIn,
    AssignmentStatus.inProgress,
    AssignmentStatus.awaitingAcceptance,
    AssignmentStatus.completed,
  };
  return assignments.any(
        (a) => activeWorkerStatuses.contains(a.assignmentStatus),
      )
      ? OrderStatus.assigned
      : status;
}

String assignmentStatusLabel(String status) => switch (status) {
  AssignmentStatus.assigned => 'Đã nhận ca',
  AssignmentStatus.checkedIn => 'Đã đến nơi',
  AssignmentStatus.inProgress => 'Đang làm việc',
  AssignmentStatus.awaitingAcceptance => 'Chờ bạn nghiệm thu',
  AssignmentStatus.completed => 'Đã xong',
  AssignmentStatus.cancelledByWorker => 'Người làm đã huỷ',
  AssignmentStatus.absent => 'Vắng mặt',
  AssignmentStatus.incident => 'Có sự cố',
  _ => status,
};

String extensionStatusLabel(String status) => switch (status) {
  ExtensionStatus.pendingPayment => 'Chờ thanh toán',
  ExtensionStatus.paid => 'Đã thanh toán, chờ người làm trả lời',
  ExtensionStatus.accepted => 'Người làm đã đồng ý',
  ExtensionStatus.declined => 'Người làm từ chối, tiền được hoàn lại',
  ExtensionStatus.expired => 'Hết hạn thanh toán',
  _ => status,
};

/// `Người làm` when the server has no name for the worker (the Workers port did not know it).
String workerName(ProgressAssignment a) =>
    (a.workerName == null || a.workerName!.trim().isEmpty)
    ? 'Người làm'
    : a.workerName!;

/// `4,8` (one decimal, Vietnamese comma); empty when there is no rating yet.
String ratingText(double rating) =>
    rating <= 0 ? '' : rating.toStringAsFixed(1).replaceAll('.', ',');

/// `1,5 giờ`, `2 giờ`.
String hoursText(double hours) =>
    '${hours == hours.roundToDouble() ? hours.toStringAsFixed(0) : hours.toString().replaceAll('.', ',')} giờ';

/// `15/10/2026 · Ca sáng` from the contract's `scheduledDate` ("yyyy-MM-dd") and shift code.
String scheduleText(String scheduledDate, String shiftCode) {
  final parts = scheduledDate.split('-');
  final date = parts.length == 3
      ? '${parts[2]}/${parts[1]}/${parts[0]}'
      : scheduledDate;
  return '$date · ${shiftLabel(shiftCode)}';
}

/// One step of the order timeline.
class TimelineStep {
  final String label;

  /// The order reached (or passed) this step.
  final bool done;

  /// This is where the order is now.
  final bool current;

  const TimelineStep({
    required this.label,
    required this.done,
    required this.current,
  });
}

/// The steps a customer follows: pay, find a worker, worker assigned, done. A cancelled order shows where it stopped as cancelled.
/// `PAID` exists only for a moment on the server (decision Q24 / B4), so it is shown as "finding a worker".
List<TimelineStep> orderTimeline(String status) {
  const labels = ['Thanh toán', 'Tìm người', 'Có người làm', 'Hoàn thành'];
  if (status == OrderStatus.cancelled) {
    return const [TimelineStep(label: 'Đã huỷ', done: true, current: true)];
  }
  final index = switch (status) {
    OrderStatus.pendingPayment => 0,
    OrderStatus.paid ||
    OrderStatus.dispatching ||
    OrderStatus.awaitingFallback => 1,
    OrderStatus.assigned => 2,
    OrderStatus.completed => 3,
    _ => 0,
  };
  final finished = status == OrderStatus.completed;
  return [
    for (var i = 0; i < labels.length; i++)
      TimelineStep(
        label: labels[i],
        done: finished || i < index,
        current: !finished && i == index,
      ),
  ];
}

/// The order can still change, so asking the server again is worth it.
bool shouldPollOrder(String status) =>
    status != OrderStatus.completed && status != OrderStatus.cancelled;

bool canPay(String status) => status == OrderStatus.pendingPayment;

/// Offered whenever the server might accept it (booking.md 3.7); the server decides, e.g. it refuses an assigned order close to its shift.
bool canCancel(String status) =>
    status != OrderStatus.completed && status != OrderStatus.cancelled;

/// The workers who are on site and can be asked to stay longer (booking.md 3.8).
List<ProgressAssignment> extendableAssignments(OrderProgress progress) => [
  for (final a in progress.assignments)
    if (a.assignmentStatus == AssignmentStatus.inProgress ||
        a.assignmentStatus == AssignmentStatus.awaitingAcceptance)
      a,
];

/// What the customer can do about one worker of the order once the job is over. The server decides the final eligibility
/// (the 48 h rating window of ratings.md 2.1, the one-dispute-per-order rule of disputes.md 2.1), so this only hides what can never apply.
enum AssignmentAction { reviewAndAccept, rate }

List<AssignmentAction> assignmentActions(String assignmentStatus) =>
    switch (assignmentStatus) {
      AssignmentStatus.awaitingAcceptance => const [
        AssignmentAction.reviewAndAccept,
      ],
      AssignmentStatus.completed => const [AssignmentAction.rate],
      _ => const [],
    };

/// A dispute is about the order and needs a job that reached the end or an absence (disputes.md 2.1: `COMPLETED`, `AWAITING_ACCEPTANCE`
/// or `ABSENT`); the 24 h window and the one ticket per order are the server's.
bool canDispute(OrderProgress progress) => progress.assignments.any(
  (a) => const {
    AssignmentStatus.awaitingAcceptance,
    AssignmentStatus.completed,
    AssignmentStatus.absent,
  }.contains(a.assignmentStatus),
);

/// "Làm lần 2" is offered only for an ASSIGNED order without an extension whose worker is on site (booking.md 3.8).
bool canExtend(OrderProgress progress) =>
    progress.orderStatus == OrderStatus.assigned &&
    progress.extension == null &&
    extendableAssignments(progress).isNotEmpty;

/// 0.5, 1, ... up to the shift length the server allows (decision Q24 / B11).
List<double> extraHourOptions(int shiftMaxHours) => [
  for (var half = 1; half <= shiftMaxHours * 2; half++) half / 2,
];

String? validateCancelReason(String text) {
  final trimmed = text.trim();
  if (trimmed.isEmpty) return 'Vui lòng nhập lý do huỷ đơn.';
  if (trimmed.length > cancelReasonMaxLength) {
    return 'Lý do tối đa $cancelReasonMaxLength ký tự (đang ${trimmed.length}).';
  }
  return null;
}

/// A cancel reason as the customer reads it.
class CancelReasonView {
  final String text;

  /// True when the order was cancelled because nobody could take it: the screen offers to book another time.
  final bool suggestRebook;

  const CancelReasonView(this.text, {this.suggestRebook = false});
}

const String noWorkerNearbyText =
    'Thật tiếc, trong bán kính 10 km hiện không tìm được người giúp việc. Bạn còn lịch nào khác không?';

/// The server writes its own cancel reasons for the system (dispatch, fallback, payment), in back-office words
/// (e.g. "Quá bán kính 10 km không tìm được thợ phù hợp (BR-03)."). They are shown in plain, kind words here;
/// a reason the customer typed is shown as typed.
CancelReasonView cancelReasonView(String raw) {
  final r = raw.trim();
  final lower = r.toLowerCase();
  if (r == 'PAYMENT_EXPIRED') {
    return const CancelReasonView(
      'Đơn tự huỷ vì quá hạn thanh toán. Bạn không bị trừ tiền.',
      suggestRebook: true,
    );
  }
  if (lower.contains('br-03') ||
      lower.contains('không tìm thấy thợ') ||
      lower.contains('không tìm được thợ')) {
    return const CancelReasonView(noWorkerNearbyText, suggestRebook: true);
  }
  if (lower.contains('từ chối đổi thợ')) {
    return const CancelReasonView(
      'Bạn đã chọn không đổi người làm sau sự cố, nên đơn được huỷ và hoàn đủ 100 %.',
      suggestRebook: true,
    );
  }
  if (lower.contains('cứu hộ') || lower.contains('br-10')) {
    return const CancelReasonView(
      'Thật tiếc, chưa tìm được người thay thế cho ca của bạn. Đơn đã huỷ và hoàn đủ 100 %. Bạn còn lịch nào khác không?',
      suggestRebook: true,
    );
  }
  if (lower.contains('agency') || lower.contains('premium')) {
    return const CancelReasonView(
      'Thật tiếc, gói Cao cấp hiện không còn đối tác trống lịch cho ca này. Bạn còn lịch nào khác không?',
      suggestRebook: true,
    );
  }
  if (lower.contains('khách không trả lời')) {
    return const CancelReasonView(
      'Đơn đã huỷ vì chưa nhận được lựa chọn của bạn trước giờ bắt đầu ca. Tiền đã được hoàn đủ 100 %.',
      suggestRebook: true,
    );
  }
  if (lower.contains('khách từ chối')) {
    return const CancelReasonView(
      'Bạn đã chọn không để 1 người làm 2 ca liên tiếp, nên đơn được huỷ và hoàn đủ 100 %.',
      suggestRebook: true,
    );
  }
  if (lower.contains('br-02')) {
    return const CancelReasonView(
      'Thật tiếc, người giúp việc không còn trống ca liền sau. Đơn đã huỷ và hoàn đủ 100 %. Bạn còn lịch nào khác không?',
      suggestRebook: true,
    );
  }
  return CancelReasonView('Lý do huỷ: $r');
}

/// True when there are more orders on the server than are shown.
bool hasMoreOrders(int shown, int total) => shown < total;

String _common(ApiException e, {required String notFound}) {
  if (e.isNetworkError) return e.message;
  if (e.isUnauthorized) {
    return 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.';
  }
  if (e.isForbidden) {
    return 'Chỉ tài khoản khách hàng mới dùng được màn hình này.';
  }
  if (e.isNotFound) return notFound;
  return 'Máy chủ gặp lỗi. Vui lòng thử lại sau.';
}

String orderLoadFailure(ApiException e) =>
    _common(e, notFound: 'Không tìm thấy đơn này trong tài khoản của bạn.');

/// A failed cancel in words; the field message of a 400 when there is one.
String cancelFailure(ApiException e) {
  if (e.statusCode == 400) {
    return e.fieldErrors?['reason']?.firstOrNull ?? 'Lý do huỷ không hợp lệ.';
  }
  if (e.statusCode == 409) {
    return 'Đơn này không huỷ được nữa: đã hoàn thành, đã huỷ, hoặc đã có người làm và còn chưa tới 2 giờ trước ca.';
  }
  return _common(
    e,
    notFound: 'Không tìm thấy đơn này trong tài khoản của bạn.',
  );
}

/// A failed "Làm lần 2" request in words, by its business code.
String extensionFailure(ApiException e) {
  if (e.statusCode == 400) {
    return e.fieldErrors?['extraHours']?.firstOrNull ??
        e.fieldErrors?['assignmentId']?.firstOrNull ??
        'Số giờ làm thêm không hợp lệ.';
  }
  if (e.statusCode == 409) {
    return switch (e.code) {
      OrderErrorCode.extensionExists =>
        'Đơn này đã có một yêu cầu làm thêm giờ.',
      OrderErrorCode.invalidState =>
        'Chỉ xin làm thêm được khi người làm đang ở nhà bạn.',
      _ => 'Chưa gửi được yêu cầu làm thêm giờ.',
    };
  }
  return _common(e, notFound: 'Không tìm thấy đơn hoặc ca làm này.');
}
