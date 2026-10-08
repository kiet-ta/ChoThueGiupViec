import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/disputes/logic/dispute_logic.dart';
import 'package:mobile/features/disputes/models/dispute_models.dart';

void main() {
  group('categories (D1)', () {
    test('a customer can dispute an absence fee, a worker cannot', () {
      expect(categoriesFor(DisputeRole.customer).map((c) => c.code), ['QUALITY', 'ATTITUDE', 'PROPERTY_DAMAGE', 'ABSENT_FEE', 'OTHER']);
      expect(categoriesFor(DisputeRole.worker).map((c) => c.code), ['QUALITY', 'ATTITUDE', 'PROPERTY_DAMAGE', 'OTHER']);
    });

    test('labels the codes and shows an unknown code as it is', () {
      expect(categoryLabel('PROPERTY_DAMAGE'), 'Hư hỏng tài sản');
      expect(categoryLabel('ABSENT_FEE'), 'Phí vắng mặt');
      expect(categoryLabel('NEW_ONE'), 'NEW_ONE');
    });

    test('each role calls its own endpoint prefix', () {
      expect(DisputeRole.customer.pathPrefix, '/api/customers/me');
      expect(DisputeRole.worker.pathPrefix, '/api/workers/me');
    });
  });

  group('labels', () {
    test('status and fault texts', () {
      expect(statusLabel(Dispute.open), 'Chờ tiếp nhận');
      expect(statusLabel(Dispute.inReview), 'Đang xử lý');
      expect(statusLabel(Dispute.resolved), 'Đã có phán quyết');
      expect(statusLabel(Dispute.dismissed), 'Đã bác bỏ');
      expect(faultLabel('FREELANCER'), contains('thợ'));
      expect(faultLabel('AGENCY'), contains('đại lý'));
      expect(faultLabel('CUSTOMER'), contains('khách hàng'));
      expect(faultLabel(null), 'Không xác định lỗi');
    });
  });

  group('validateDispute', () {
    test('accepts a complete dispute', () {
      expect(validateDispute(DisputeRole.customer, 'QUALITY', 'Dọn chưa sạch', 1).isEmpty, isTrue);
      expect(validateDispute(DisputeRole.customer, 'ABSENT_FEE', 'x', 10).isEmpty, isTrue);
    });

    test('needs a category of the role', () {
      expect(validateDispute(DisputeRole.customer, null, 'x', 1).category, isNotNull);
      expect(validateDispute(DisputeRole.customer, 'NOPE', 'x', 1).category, isNotNull);
      expect(validateDispute(DisputeRole.worker, 'ABSENT_FEE', 'x', 1).category, isNotNull);
    });

    test('limits the description to 1000 characters counted after trimming and refuses blank', () {
      expect(validateDispute(DisputeRole.customer, 'OTHER', '   ', 1).description, isNotNull);
      expect(validateDispute(DisputeRole.customer, 'OTHER', 'x' * 1000, 1).description, isNull);
      expect(validateDispute(DisputeRole.customer, 'OTHER', '  ${'x' * 1000}  ', 1).description, isNull);
      expect(validateDispute(DisputeRole.customer, 'OTHER', 'x' * 1001, 1).description, contains('1001'));
    });

    test('needs between 1 and 10 evidence photos', () {
      expect(validateDispute(DisputeRole.customer, 'OTHER', 'x', 0).evidence, isNotNull);
      expect(validateDispute(DisputeRole.customer, 'OTHER', 'x', 1).evidence, isNull);
      expect(validateDispute(DisputeRole.customer, 'OTHER', 'x', 10).evidence, isNull);
      expect(validateDispute(DisputeRole.customer, 'OTHER', 'x', 11).evidence, isNotNull);
    });
  });

  group('buildDispute', () {
    test('trims the description and copies the evidence addresses', () {
      final urls = ['a', 'b'];
      final body = buildDispute(42, 'QUALITY', '  Dọn chưa sạch  ', urls).toJson();
      urls.add('c');

      expect(body, {
        'orderId': 42,
        'category': 'QUALITY',
        'description': 'Dọn chưa sạch',
        'evidenceUrls': ['a', 'b'],
      });
    });
  });

  group('disputeFailure', () {
    test('a 409 explains the three causes', () {
      final f = disputeFailure(const ApiException(409, 'x'));

      expect(f.message, contains('24 giờ'));
      expect(f.message, contains('đã có khiếu nại'));
    });

    test('a 404 says the order is not the caller\'s', () {
      expect(disputeFailure(const ApiException(404, 'x')).message, contains('Không tìm thấy'));
    });

    test('a 400 keeps the field messages and points at them', () {
      final f = disputeFailure(const ApiException(400, 'Validation failed', fieldErrors: {
        'category': ['Category is not allowed.'],
        'evidenceUrls': ['At least one evidence photo is required.'],
      }));

      expect(f.fields['category'], 'Category is not allowed.');
      expect(f.fields['evidenceUrls'], 'At least one evidence photo is required.');
      expect(f.message, contains('ô báo lỗi'));
    });

    test('a 400 without fields and other statuses show the server message', () {
      expect(disputeFailure(const ApiException(400, 'Bad body')).message, 'Bad body');
      expect(disputeFailure(const ApiException(0, 'Không kết nối được máy chủ.')).message, 'Không kết nối được máy chủ.');
      expect(disputeFailure(const ApiException(500, 'Boom')).message, 'Boom');
    });
  });

  group('Dispute.fromJson', () {
    test('reads the contract shape of a resolved dispute', () {
      final d = Dispute.fromJson({
        'disputeId': 9,
        'orderId': 42,
        'raisedBy': 'CUSTOMER',
        'category': 'QUALITY',
        'description': 'x',
        'evidenceUrls': ['a', 'b'],
        'disputeStatus': 'RESOLVED',
        'faultParty': 'FREELANCER',
        'compensationAmount': 150000,
        'slaDueAt': '2026-10-10T03:00:00Z',
        'resolvedAt': '2026-10-09T03:00:00Z',
        'createdAt': '2026-10-08T03:00:00Z',
      });

      expect(d.disputeId, 9);
      expect(d.evidenceUrls, ['a', 'b']);
      expect(d.faultParty, 'FREELANCER');
      expect(d.compensationAmount, 150000);
      expect(d.resolvedAt, DateTime.utc(2026, 10, 9, 3));
    });

    test('tolerates nulls of an open dispute', () {
      final d = Dispute.fromJson({'disputeId': 1, 'disputeStatus': 'OPEN', 'faultParty': null, 'compensationAmount': null, 'resolvedAt': null});

      expect(d.faultParty, isNull);
      expect(d.compensationAmount, isNull);
      expect(d.resolvedAt, isNull);
      expect(d.evidenceUrls, isEmpty);
    });
  });
}
