import '../../../core/network/api_client.dart';
import '../models/dispute_models.dart';

// Pure helpers of the dispute screens: categories, labels, validation and error texts.

const int maxDescriptionLength = 1000;
const int maxEvidenceCount = 10;

/// Categories the person can choose (D1); only a customer can dispute an absence fee.
List<DisputeCategory> categoriesFor(DisputeRole role) => [
      DisputeCategory.quality,
      DisputeCategory.attitude,
      DisputeCategory.propertyDamage,
      if (role == DisputeRole.customer) DisputeCategory.absentFee,
      DisputeCategory.other,
    ];

String categoryLabel(String code) {
  for (final c in [
    DisputeCategory.quality,
    DisputeCategory.attitude,
    DisputeCategory.propertyDamage,
    DisputeCategory.absentFee,
    DisputeCategory.other,
  ]) {
    if (c.code == code) return c.label;
  }
  return code;
}

String statusLabel(String status) {
  switch (status) {
    case Dispute.open:
      return 'Chờ tiếp nhận';
    case Dispute.inReview:
      return 'Đang xử lý';
    case Dispute.resolved:
      return 'Đã có phán quyết';
    case Dispute.dismissed:
      return 'Đã bác bỏ';
    default:
      return status;
  }
}

/// Who was found at fault, as the party reads it.
String faultLabel(String? faultParty) {
  switch (faultParty) {
    case 'FREELANCER':
      return 'Lỗi thuộc về thợ';
    case 'AGENCY':
      return 'Lỗi thuộc về đại lý';
    case 'CUSTOMER':
      return 'Lỗi thuộc về khách hàng';
    default:
      return 'Không xác định lỗi';
  }
}

/// Field errors of a form, empty when everything is valid.
class DisputeErrors {
  final String? category;
  final String? description;
  final String? evidence;

  const DisputeErrors({this.category, this.description, this.evidence});

  bool get isEmpty => category == null && description == null && evidence == null;
}

DisputeErrors validateDispute(DisputeRole role, String? category, String description, int evidenceCount) {
  final trimmed = description.trim();
  return DisputeErrors(
    category: category == null || !categoriesFor(role).any((c) => c.code == category) ? 'Chọn loại khiếu nại.' : null,
    description: trimmed.isEmpty
        ? 'Mô tả sự việc.'
        : trimmed.length > maxDescriptionLength
            ? 'Mô tả tối đa $maxDescriptionLength ký tự (đang ${trimmed.length}).'
            : null,
    evidence: evidenceCount < 1
        ? 'Cần ít nhất 1 ảnh bằng chứng.'
        : evidenceCount > maxEvidenceCount
            ? 'Tối đa $maxEvidenceCount ảnh.'
            : null,
  );
}

DisputeSubmission buildDispute(int orderId, String category, String description, List<String> evidenceUrls) =>
    DisputeSubmission(orderId: orderId, category: category, description: description.trim(), evidenceUrls: List.of(evidenceUrls));

/// A failed send: the text for the person and the messages the server gave per field.
class DisputeFailure {
  final String message;
  final Map<String, String> fields;

  const DisputeFailure(this.message, {this.fields = const {}});
}

DisputeFailure disputeFailure(ApiException e) {
  if (e.isConflict) {
    return const DisputeFailure(
        'Không gửi được: đã quá 24 giờ sau ca làm, ca này chưa có gì để khiếu nại, hoặc đơn này đã có khiếu nại (mỗi đơn chỉ có một khiếu nại).');
  }
  if (e.isNotFound) return const DisputeFailure('Không tìm thấy đơn này trong các ca của bạn.');
  if (e.statusCode == 400) {
    final fields = <String, String>{};
    e.fieldErrors?.forEach((key, messages) {
      if (messages.isNotEmpty) fields[key] = messages.first;
    });
    return DisputeFailure(fields.isEmpty ? e.message : 'Vui lòng kiểm tra các ô báo lỗi bên dưới.', fields: fields);
  }
  return DisputeFailure(e.message);
}
