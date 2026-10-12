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
      OrderStatus.dispatching => 'Đang tìm thợ',
      OrderStatus.assigned => 'Đã có thợ',
      OrderStatus.completed => 'Hoàn thành',
      OrderStatus.cancelled => 'Đã huỷ',
      _ => status,
    };

String assignmentStatusLabel(String status) => switch (status) {
      AssignmentStatus.assigned => 'Đã nhận ca',
      AssignmentStatus.checkedIn => 'Đã đến nơi',
      AssignmentStatus.inProgress => 'Đang làm việc',
      AssignmentStatus.awaitingAcceptance => 'Chờ bạn nghiệm thu',
      AssignmentStatus.completed => 'Đã xong',
      AssignmentStatus.cancelledByWorker => 'Thợ đã huỷ',
      AssignmentStatus.absent => 'Vắng mặt',
      AssignmentStatus.incident => 'Có sự cố',
      _ => status,
    };

String extensionStatusLabel(String status) => switch (status) {
      ExtensionStatus.pendingPayment => 'Chờ thanh toán',
      ExtensionStatus.paid => 'Đã thanh toán, chờ thợ trả lời',
      ExtensionStatus.accepted => 'Thợ đã đồng ý',
      ExtensionStatus.declined => 'Thợ từ chối, tiền được hoàn lại',
      ExtensionStatus.expired => 'Hết hạn thanh toán',
      _ => status,
    };

/// `Thợ` when the server has no name for the worker (the Workers port did not know it).
String workerName(ProgressAssignment a) => (a.workerName == null || a.workerName!.trim().isEmpty) ? 'Thợ' : a.workerName!;

/// `4,8` (one decimal, Vietnamese comma); empty when there is no rating yet.
String ratingText(double rating) => rating <= 0 ? '' : rating.toStringAsFixed(1).replaceAll('.', ',');

/// `1,5 giờ`, `2 giờ`.
String hoursText(double hours) =>
    '${hours == hours.roundToDouble() ? hours.toStringAsFixed(0) : hours.toString().replaceAll('.', ',')} giờ';

/// `15/10/2026 · Ca sáng` from the contract's `scheduledDate` ("yyyy-MM-dd") and shift code.
String scheduleText(String scheduledDate, String shiftCode) {
  final parts = scheduledDate.split('-');
  final date = parts.length == 3 ? '${parts[2]}/${parts[1]}/${parts[0]}' : scheduledDate;
  return '$date · ${shiftLabel(shiftCode)}';
}

/// One step of the order timeline.
class TimelineStep {
  final String label;

  /// The order reached (or passed) this step.
  final bool done;

  /// This is where the order is now.
  final bool current;

  const TimelineStep({required this.label, required this.done, required this.current});
}

/// The steps a customer follows: pay, find a worker, worker assigned, done. A cancelled order shows where it stopped as cancelled.
/// `PAID` exists only for a moment on the server (decision Q24 / B4), so it is shown as "finding a worker".
List<TimelineStep> orderTimeline(String status) {
  const labels = ['Thanh toán', 'Tìm thợ', 'Có thợ', 'Hoàn thành'];
  if (status == OrderStatus.cancelled) {
    return const [TimelineStep(label: 'Đã huỷ', done: true, current: true)];
  }
  final index = switch (status) {
    OrderStatus.pendingPayment => 0,
    OrderStatus.paid || OrderStatus.dispatching => 1,
    OrderStatus.assigned => 2,
    OrderStatus.completed => 3,
    _ => 0,
  };
  final finished = status == OrderStatus.completed;
  return [
    for (var i = 0; i < labels.length; i++)
      TimelineStep(label: labels[i], done: finished || i < index, current: !finished && i == index),
  ];
}

/// The order can still change, so asking the server again is worth it.
bool shouldPollOrder(String status) => status != OrderStatus.completed && status != OrderStatus.cancelled;

bool canPay(String status) => status == OrderStatus.pendingPayment;

/// Offered whenever the server might accept it (booking.md 3.7); the server decides, e.g. it refuses an assigned order close to its shift.
bool canCancel(String status) => status != OrderStatus.completed && status != OrderStatus.cancelled;

/// The workers who are on site and can be asked to stay longer (booking.md 3.8).
List<ProgressAssignment> extendableAssignments(OrderProgress progress) => [
      for (final a in progress.assignments)
        if (a.assignmentStatus == AssignmentStatus.inProgress || a.assignmentStatus == AssignmentStatus.awaitingAcceptance) a,
    ];

/// "Làm lần 2" is offered only for an ASSIGNED order without an extension whose worker is on site (booking.md 3.8).
bool canExtend(OrderProgress progress) =>
    progress.orderStatus == OrderStatus.assigned && progress.extension == null && extendableAssignments(progress).isNotEmpty;

/// 0.5, 1, ... up to the shift length the server allows (decision Q24 / B11).
List<double> extraHourOptions(int shiftMaxHours) => [for (var half = 1; half <= shiftMaxHours * 2; half++) half / 2];

String? validateCancelReason(String text) {
  final trimmed = text.trim();
  if (trimmed.isEmpty) return 'Vui lòng nhập lý do huỷ đơn.';
  if (trimmed.length > cancelReasonMaxLength) return 'Lý do tối đa $cancelReasonMaxLength ký tự (đang ${trimmed.length}).';
  return null;
}

/// True when there are more orders on the server than are shown.
bool hasMoreOrders(int shown, int total) => shown < total;

String _common(ApiException e, {required String notFound}) {
  if (e.isNetworkError) return e.message;
  if (e.isUnauthorized) return 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.';
  if (e.isForbidden) return 'Chỉ tài khoản khách hàng mới dùng được màn hình này.';
  if (e.isNotFound) return notFound;
  return 'Máy chủ gặp lỗi. Vui lòng thử lại sau.';
}

String orderLoadFailure(ApiException e) => _common(e, notFound: 'Không tìm thấy đơn này trong tài khoản của bạn.');

/// A failed cancel in words; the field message of a 400 when there is one.
String cancelFailure(ApiException e) {
  if (e.statusCode == 400) return e.fieldErrors?['reason']?.firstOrNull ?? 'Lý do huỷ không hợp lệ.';
  if (e.statusCode == 409) {
    return 'Đơn này không huỷ được nữa: đã hoàn thành, đã huỷ, hoặc đã có thợ và còn chưa tới 2 giờ trước ca.';
  }
  return _common(e, notFound: 'Không tìm thấy đơn này trong tài khoản của bạn.');
}

/// A failed "Làm lần 2" request in words, by its business code.
String extensionFailure(ApiException e) {
  if (e.statusCode == 400) {
    return e.fieldErrors?['extraHours']?.firstOrNull ?? e.fieldErrors?['assignmentId']?.firstOrNull ?? 'Số giờ làm thêm không hợp lệ.';
  }
  if (e.statusCode == 409) {
    return switch (e.code) {
      OrderErrorCode.extensionExists => 'Đơn này đã có một yêu cầu làm thêm giờ.',
      OrderErrorCode.invalidState => 'Chỉ yêu cầu làm thêm được khi thợ đang làm việc tại nhà bạn.',
      _ => 'Chưa gửi được yêu cầu làm thêm giờ.',
    };
  }
  return _common(e, notFound: 'Không tìm thấy đơn hoặc ca làm này.');
}
