import 'package:flutter/material.dart';
import '../../../core/theme/nordic_colors.dart';

/// Customer profile data shape per .spec/contracts/customers.md §1 & §2.1.
class CustomerProfile {
  final int customerId;
  final String phoneNumber;
  final String fullName;
  final String? email;
  final double trustScore;
  final String accountStatus;
  final DateTime? createdAt;

  const CustomerProfile({
    required this.customerId,
    required this.phoneNumber,
    required this.fullName,
    this.email,
    required this.trustScore,
    required this.accountStatus,
    this.createdAt,
  });

  factory CustomerProfile.fromJson(Map<String, dynamic> json) {
    return CustomerProfile(
      customerId: (json['customerId'] as num?)?.toInt() ?? 0,
      phoneNumber: json['phoneNumber'] as String? ?? '',
      fullName: json['fullName'] as String? ?? '',
      email: json['email'] as String?,
      trustScore: (json['trustScore'] as num?)?.toDouble() ?? 5.0,
      accountStatus: json['accountStatus'] as String? ?? 'ACTIVE',
      createdAt: json['createdAt'] != null
          ? DateTime.tryParse(json['createdAt'] as String)
          : null,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'customerId': customerId,
      'phoneNumber': phoneNumber,
      'fullName': fullName,
      'email': email,
      'trustScore': trustScore,
      'accountStatus': accountStatus,
      if (createdAt != null) 'createdAt': createdAt!.toIso8601String(),
    };
  }

  CustomerProfile copyWith({
    int? customerId,
    String? phoneNumber,
    String? fullName,
    String? email,
    double? trustScore,
    String? accountStatus,
    DateTime? createdAt,
  }) {
    return CustomerProfile(
      customerId: customerId ?? this.customerId,
      phoneNumber: phoneNumber ?? this.phoneNumber,
      fullName: fullName ?? this.fullName,
      email: email ?? this.email,
      trustScore: trustScore ?? this.trustScore,
      accountStatus: accountStatus ?? this.accountStatus,
      createdAt: createdAt ?? this.createdAt,
    );
  }

  String get statusDisplayName {
    switch (accountStatus.toUpperCase()) {
      case 'ACTIVE':
        return 'Hoạt động';
      case 'LOCKED':
        return 'Đã khoá';
      default:
        return accountStatus;
    }
  }

  Color get statusBadgeColor {
    switch (accountStatus.toUpperCase()) {
      case 'ACTIVE':
        return const Color(0xFF2E7D32);
      case 'LOCKED':
        return NordicColors.error;
      default:
        return NordicColors.textSecondary;
    }
  }

  Color get statusBadgeBgColor {
    switch (accountStatus.toUpperCase()) {
      case 'ACTIVE':
        return const Color(0xFFE8F5E9);
      case 'LOCKED':
        return const Color(0xFFFEE2E2);
      default:
        return NordicColors.surfaceSubtle;
    }
  }

  String get trustScoreTierName {
    if (trustScore >= 4.8) {
      return 'Xuất sắc';
    } else if (trustScore >= 4.0) {
      return 'Tốt';
    } else {
      return 'Tiêu chuẩn';
    }
  }

  String get initialLetter {
    final trimmed = fullName.trim();
    if (trimmed.isNotEmpty) {
      return trimmed[0].toUpperCase();
    }
    return 'K';
  }

  String get formattedCreatedAt {
    if (createdAt == null) return 'Mới gia nhập';
    final d = createdAt!.day.toString().padLeft(2, '0');
    final m = createdAt!.month.toString().padLeft(2, '0');
    final y = createdAt!.year.toString();
    return '$d/$m/$y';
  }
}
