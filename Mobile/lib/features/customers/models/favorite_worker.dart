import 'package:flutter/material.dart';
import '../../../core/theme/nordic_colors.dart';

/// Worker profile read shape from .spec/contracts/customers.md §1 & §2.3.
class FavoriteWorker {
  final int workerId;
  final String fullName;
  final double ratingAvg;
  final int completedJobs;
  final String workStatus;
  final DateTime? addedAt;

  const FavoriteWorker({
    required this.workerId,
    required this.fullName,
    required this.ratingAvg,
    required this.completedJobs,
    required this.workStatus,
    this.addedAt,
  });

  factory FavoriteWorker.fromJson(Map<String, dynamic> json) {
    return FavoriteWorker(
      workerId: (json['workerId'] as num?)?.toInt() ?? 0,
      fullName: json['fullName'] as String? ?? 'Chuyên viên',
      ratingAvg: (json['ratingAvg'] as num?)?.toDouble() ?? 0.0,
      completedJobs: (json['completedJobs'] as num?)?.toInt() ?? 0,
      workStatus: json['workStatus'] as String? ?? 'ACTIVE',
      addedAt: json['addedAt'] != null ? DateTime.tryParse(json['addedAt'] as String) : null,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'workerId': workerId,
      'fullName': fullName,
      'ratingAvg': ratingAvg,
      'completedJobs': completedJobs,
      'workStatus': workStatus,
      if (addedAt != null) 'addedAt': addedAt!.toIso8601String(),
    };
  }

  FavoriteWorker copyWith({
    int? workerId,
    String? fullName,
    double? ratingAvg,
    int? completedJobs,
    String? workStatus,
    DateTime? addedAt,
  }) {
    return FavoriteWorker(
      workerId: workerId ?? this.workerId,
      fullName: fullName ?? this.fullName,
      ratingAvg: ratingAvg ?? this.ratingAvg,
      completedJobs: completedJobs ?? this.completedJobs,
      workStatus: workStatus ?? this.workStatus,
      addedAt: addedAt ?? this.addedAt,
    );
  }

  String get statusDisplayName {
    switch (workStatus.toUpperCase()) {
      case 'ACTIVE':
        return 'Sẵn sàng nhận ca';
      case 'BUSY':
        return 'Đang trong ca làm';
      case 'OFFLINE':
      default:
        return 'Nghỉ ca';
    }
  }

  Color get statusColor {
    switch (workStatus.toUpperCase()) {
      case 'ACTIVE':
        return NordicColors.success;
      case 'BUSY':
        return NordicColors.accentAmber;
      case 'OFFLINE':
      default:
        return NordicColors.textSecondary;
    }
  }
}
