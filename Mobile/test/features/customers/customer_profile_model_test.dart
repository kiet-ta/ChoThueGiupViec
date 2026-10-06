import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/customers/models/customer_profile.dart';

void main() {
  group('CustomerProfile Model Tests', () {
    test('fromJson and toJson roundtrip preserves contract fields', () {
      final json = {
        'customerId': 42,
        'phoneNumber': '0909888999',
        'fullName': 'Đặng Hoàng Nam',
        'email': 'nam.dang@example.com',
        'trustScore': 4.85,
        'accountStatus': 'ACTIVE',
        'createdAt': '2026-02-15T08:30:00.000Z',
      };

      final profile = CustomerProfile.fromJson(json);

      expect(profile.customerId, equals(42));
      expect(profile.phoneNumber, equals('0909888999'));
      expect(profile.fullName, equals('Đặng Hoàng Nam'));
      expect(profile.email, equals('nam.dang@example.com'));
      expect(profile.trustScore, equals(4.85));
      expect(profile.accountStatus, equals('ACTIVE'));
      expect(profile.createdAt, isNotNull);
      expect(profile.createdAt!.year, equals(2026));

      final output = profile.toJson();
      expect(output['customerId'], equals(42));
      expect(output['phoneNumber'], equals('0909888999'));
      expect(output['fullName'], equals('Đặng Hoàng Nam'));
      expect(output['email'], equals('nam.dang@example.com'));
      expect(output['trustScore'], equals(4.85));
      expect(output['accountStatus'], equals('ACTIVE'));
      expect(output['createdAt'], isNotNull);
    });

    test('copyWith updates specified fields', () {
      final profile = CustomerProfile(
        customerId: 1,
        phoneNumber: '0901234567',
        fullName: 'Initial Name',
        email: 'old@example.com',
        trustScore: 5.0,
        accountStatus: 'ACTIVE',
        createdAt: DateTime(2026, 1, 1),
      );

      final updated = profile.copyWith(
        fullName: 'Updated Name',
        email: 'new@example.com',
      );

      expect(updated.fullName, equals('Updated Name'));
      expect(updated.email, equals('new@example.com'));
      expect(updated.phoneNumber, equals('0901234567'));
      expect(updated.customerId, equals(1));
    });

    test('helper getters format badges and initials correctly', () {
      final activeProfile = CustomerProfile(
        customerId: 1,
        phoneNumber: '0901234567',
        fullName: 'Nguyễn Văn An',
        trustScore: 4.9,
        accountStatus: 'ACTIVE',
        createdAt: DateTime(2026, 3, 10),
      );

      expect(activeProfile.statusDisplayName, equals('Hoạt động'));
      expect(activeProfile.statusBadgeColor, equals(const Color(0xFF2E7D32)));
      expect(activeProfile.trustScoreTierName, equals('Xuất sắc'));
      expect(activeProfile.initialLetter, equals('N'));
      expect(activeProfile.formattedCreatedAt, equals('10/03/2026'));

      final lockedProfile = CustomerProfile(
        customerId: 2,
        phoneNumber: '0909999999',
        fullName: 'Trần B',
        trustScore: 3.5,
        accountStatus: 'LOCKED',
      );

      expect(lockedProfile.statusDisplayName, equals('Đã khoá'));
      expect(lockedProfile.trustScoreTierName, equals('Tiêu chuẩn'));
      expect(lockedProfile.formattedCreatedAt, equals('Mới gia nhập'));
    });
  });
}
