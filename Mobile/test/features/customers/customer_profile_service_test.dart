import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/customers/services/customer_profile_service.dart';

void main() {
  group('CustomerProfileService Tests', () {
    late CustomerProfileService service;

    setUp(() {
      service = CustomerProfileService(isMock: true);
    });

    test('getProfile returns valid default customer profile', () async {
      final profile = await service.getProfile();

      expect(profile.customerId, equals(1));
      expect(profile.phoneNumber, equals('0901234567'));
      expect(profile.fullName, equals('Nguyễn Văn An'));
      expect(profile.accountStatus, equals('ACTIVE'));
      expect(profile.trustScore, equals(5.0));
    });

    test('updateProfile updates fullName and email successfully', () async {
      final updated = await service.updateProfile(
        fullName: 'Lê Văn Cường',
        email: 'cuong.le@example.com',
      );

      expect(updated.fullName, equals('Lê Văn Cường'));
      expect(updated.email, equals('cuong.le@example.com'));
      expect(updated.phoneNumber, equals('0901234567'));

      final retrieved = await service.getProfile();
      expect(retrieved.fullName, equals('Lê Văn Cường'));
      expect(retrieved.email, equals('cuong.le@example.com'));
    });

    test('updateProfile rejects empty fullName', () async {
      expect(
        () => service.updateProfile(fullName: '   '),
        throwsA(isA<ArgumentError>().having(
          (e) => e.message,
          'message',
          contains('Họ và tên không được để trống'),
        )),
      );
    });

    test('updateProfile rejects fullName exceeding 100 characters', () async {
      final longName = 'A' * 101;
      expect(
        () => service.updateProfile(fullName: longName),
        throwsA(isA<ArgumentError>().having(
          (e) => e.message,
          'message',
          contains('không được vượt quá 100 ký tự'),
        )),
      );
    });

    test('updateProfile rejects invalid email format', () async {
      expect(
        () => service.updateProfile(fullName: 'Valid Name', email: 'invalid-email'),
        throwsA(isA<ArgumentError>().having(
          (e) => e.message,
          'message',
          contains('Email không đúng định dạng'),
        )),
      );
    });

    test('updateProfile rejects email exceeding 255 characters', () async {
      final longEmail = '${'a' * 250}@example.com';
      expect(
        () => service.updateProfile(fullName: 'Valid Name', email: longEmail),
        throwsA(isA<ArgumentError>().having(
          (e) => e.message,
          'message',
          contains('Email không được vượt quá 255 ký tự'),
        )),
      );
    });
  });
}
