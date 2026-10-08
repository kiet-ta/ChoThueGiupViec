import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/dispatch/models/check_in_result.dart';

void main() {
  group('CheckInResult Model Tests', () {
    test('CheckInResult fromJson parses GPS verified result correctly', () {
      final json = {
        'checkInId': 2001,
        'assignmentId': 1001,
        'checkedInAt': '2026-10-15T07:55:00Z',
        'distanceMeters': 45.2,
        'isGpsVerified': true,
        'requiresAlternativeVerification': false,
        'verificationMethod': 'GPS',
        'platePhotoUrl': null,
        'isCustomerConfirmed': false,
      };

      final result = CheckInResult.fromJson(json);

      expect(result.checkInId, 2001);
      expect(result.assignmentId, 1001);
      expect(result.distanceMeters, 45.2);
      expect(result.isGpsVerified, isTrue);
      expect(result.requiresAlternativeVerification, isFalse);
      expect(result.verificationMethod, 'GPS');
      expect(result.platePhotoUrl, isNull);
      expect(result.isCustomerConfirmed, isFalse);
    });

    test('CheckInResult fromJson parses fallback verification accurately', () {
      final json = {
        'checkInId': 2002,
        'assignmentId': 1001,
        'checkedInAt': '2026-10-15T07:58:00Z',
        'distanceMeters': 180.0,
        'isGpsVerified': false,
        'requiresAlternativeVerification': false,
        'verificationMethod': 'PLATE_PHOTO',
        'platePhotoUrl': 'https://storage.local/plates/plate_1001.jpg',
        'isCustomerConfirmed': false,
      };

      final result = CheckInResult.fromJson(json);

      expect(result.checkInId, 2002);
      expect(result.isGpsVerified, isFalse);
      expect(result.verificationMethod, 'PLATE_PHOTO');
      expect(result.platePhotoUrl, 'https://storage.local/plates/plate_1001.jpg');
    });

    test('CheckInResult roundtrip toJson maintains data integrity', () {
      final item = CheckInResult(
        checkInId: 3001,
        assignmentId: 1002,
        checkedInAt: DateTime.utc(2026, 10, 15, 8, 0),
        distanceMeters: 30.5,
        isGpsVerified: true,
        requiresAlternativeVerification: false,
        verificationMethod: 'GPS',
        isCustomerConfirmed: false,
      );

      final json = item.toJson();
      final roundtrip = CheckInResult.fromJson(json);

      expect(roundtrip.checkInId, 3001);
      expect(roundtrip.distanceMeters, 30.5);
      expect(roundtrip.isGpsVerified, isTrue);
    });
  });
}
