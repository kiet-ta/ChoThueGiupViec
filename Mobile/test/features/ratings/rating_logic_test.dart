import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/ratings/logic/rating_logic.dart';
import 'package:mobile/features/ratings/models/rating_models.dart';

void main() {
  group('countdownText', () {
    test('shows hours and minutes of the 48 h window', () {
      expect(countdownText(47 * 3600 + 12 * 60), 'Còn 47 giờ 12 phút');
      expect(countdownText(3600), 'Còn 1 giờ 0 phút');
      expect(countdownText(48 * 3600), 'Còn 48 giờ 0 phút');
    });

    test('shows only minutes under an hour and says under a minute', () {
      expect(countdownText(59 * 60), 'Còn 59 phút');
      expect(countdownText(60), 'Còn 1 phút');
      expect(countdownText(59), 'Còn dưới 1 phút');
      expect(countdownText(1), 'Còn dưới 1 phút');
    });

    test('says it is over at zero and below', () {
      expect(countdownText(0), 'Đã hết hạn đánh giá');
      expect(countdownText(-5), 'Đã hết hạn đánh giá');
    });
  });

  group('reasonMessage', () {
    test('explains each closed reason and has none for OPEN or an unknown value', () {
      expect(reasonMessage(RatingWindow.notCompleted), contains('chưa hoàn thành'));
      expect(reasonMessage(RatingWindow.windowClosed), contains('48 giờ'));
      expect(reasonMessage(RatingWindow.alreadyRated), contains('đã đánh giá'));
      expect(reasonMessage(RatingWindow.open), isNull);
      expect(reasonMessage('SOMETHING'), isNull);
    });
  });

  group('role criteria (decision Q14)', () {
    test('the customer rates three fixed criteria and the worker two', () {
      expect(RatingRole.customer.criteria.map((c) => c.key), ['punctuality', 'cleaningQuality', 'attitude']);
      expect(RatingRole.worker.criteria.map((c) => c.key), ['cooperation', 'workingConditions']);
    });

    test('each role calls its own endpoint prefix and only the worker rating is internal', () {
      expect(RatingRole.customer.pathPrefix, '/api/customers/me');
      expect(RatingRole.worker.pathPrefix, '/api/workers/me');
      expect(RatingRole.customer.isInternalOnly, isFalse);
      expect(RatingRole.worker.isInternalOnly, isTrue);
    });
  });

  group('validateRating', () {
    const full = {'punctuality': 5, 'cleaningQuality': 4, 'attitude': 5};

    test('accepts a complete customer rating and a complete worker rating', () {
      expect(validateRating(RatingRole.customer, 5, full, '').isEmpty, isTrue);
      expect(validateRating(RatingRole.worker, 1, {'cooperation': 1, 'workingConditions': 5}, 'ok').isEmpty, isTrue);
    });

    test('needs the overall stars between 1 and 5', () {
      expect(validateRating(RatingRole.customer, null, full, '').stars, isNotNull);
      expect(validateRating(RatingRole.customer, 0, full, '').stars, isNotNull);
      expect(validateRating(RatingRole.customer, 6, full, '').stars, isNotNull);
      expect(validateRating(RatingRole.customer, 1, full, '').stars, isNull);
      expect(validateRating(RatingRole.customer, 5, full, '').stars, isNull);
    });

    test('needs every fixed criterion, each from 1 to 5', () {
      final errors = validateRating(RatingRole.customer, 5, {'punctuality': 5, 'attitude': 9}, '');

      expect(errors.criteria.keys, containsAll(['cleaningQuality', 'attitude']));
      expect(errors.criteria.containsKey('punctuality'), isFalse);
      expect(validateRating(RatingRole.customer, 5, {...full, 'punctuality': 0}, '').criteria.keys, ['punctuality']);
    });

    test('a worker needs the worker criteria, not the customer ones', () {
      final errors = validateRating(RatingRole.worker, 4, full, '');

      expect(errors.criteria.keys, ['cooperation', 'workingConditions']);
    });

    test('limits the comment to 500 characters counted after trimming', () {
      expect(validateRating(RatingRole.customer, 5, full, 'x' * 500).comment, isNull);
      expect(validateRating(RatingRole.customer, 5, full, '  ${'x' * 500}  ').comment, isNull);
      expect(validateRating(RatingRole.customer, 5, full, 'x' * 501).comment, contains('501'));
      expect(validateRating(RatingRole.customer, 5, full, '   ').comment, isNull);
    });
  });

  group('buildSubmission', () {
    test('sends only the fixed keys of the role and a trimmed comment', () {
      final body = buildSubmission(
        RatingRole.worker,
        4,
        {'cooperation': 5, 'workingConditions': 3, 'punctuality': 1},
        '  Gia chủ thân thiện  ',
      ).toJson();

      expect(body['stars'], 4);
      expect(body['criteria'], {'cooperation': 5, 'workingConditions': 3});
      expect(body['comment'], 'Gia chủ thân thiện');
    });

    test('sends null for a blank comment', () {
      final body = buildSubmission(RatingRole.customer, 5, {'punctuality': 5, 'cleaningQuality': 5, 'attitude': 5}, '   ').toJson();

      expect(body['comment'], isNull);
    });
  });

  group('ratingFailure', () {
    test('a 409 reloads the window and explains the three causes', () {
      final failure = ratingFailure(const ApiException(409, 'x'));

      expect(failure.reloadWindow, isTrue);
      expect(failure.message, contains('48 giờ'));
    });

    test('a 400 keeps the field messages and points at them', () {
      final failure = ratingFailure(const ApiException(400, 'Validation failed', fieldErrors: {
        'stars': ['Stars must be an integer between 1 and 5.'],
        'criteria.attitude': ['This criterion is required.'],
      }));

      expect(failure.fields['stars'], 'Stars must be an integer between 1 and 5.');
      expect(failure.fields['criteria.attitude'], 'This criterion is required.');
      expect(failure.message, contains('ô báo lỗi'));
      expect(failure.reloadWindow, isFalse);
    });

    test('a 400 without fields shows the server message', () {
      expect(ratingFailure(const ApiException(400, 'Bad body')).message, 'Bad body');
    });

    test('404, a network failure and any other status have their own text', () {
      expect(ratingFailure(const ApiException(404, 'x')).message, contains('Không tìm thấy'));
      expect(ratingFailure(const ApiException(0, 'Không kết nối được máy chủ.')).message, 'Không kết nối được máy chủ.');
      expect(ratingFailure(const ApiException(500, 'Boom')).message, 'Boom');
    });
  });

  group('RatingWindow.fromJson', () {
    test('reads the contract shape', () {
      final w = RatingWindow.fromJson({
        'assignmentId': 7,
        'canRate': true,
        'reason': 'OPEN',
        'opensAt': '2026-10-07T03:00:00Z',
        'closesAt': '2026-10-09T03:00:00Z',
        'secondsRemaining': 170000,
      });

      expect(w.assignmentId, 7);
      expect(w.canRate, isTrue);
      expect(w.reason, 'OPEN');
      expect(w.closesAt, DateTime.utc(2026, 10, 9, 3));
      expect(w.secondsRemaining, 170000);
    });

    test('tolerates nulls for a window that is not open', () {
      final w = RatingWindow.fromJson({'assignmentId': 7, 'canRate': false, 'reason': 'NOT_COMPLETED', 'opensAt': null, 'closesAt': null, 'secondsRemaining': 0});

      expect(w.canRate, isFalse);
      expect(w.opensAt, isNull);
      expect(w.closesAt, isNull);
    });
  });
}
