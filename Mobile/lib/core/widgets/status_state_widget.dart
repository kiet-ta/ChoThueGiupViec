import 'package:flutter/material.dart';
import '../theme/nordic_colors.dart';
import '../theme/nordic_typography.dart';

/// Loading state indicator with optional descriptive message.
class LoadingStateWidget extends StatelessWidget {
  final String? message;
  final double size;

  const LoadingStateWidget({
    super.key,
    this.message,
    this.size = 36.0,
  });

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24.0),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            SizedBox(
              width: size,
              height: size,
              child: const CircularProgressIndicator(
                strokeWidth: 3.0,
                valueColor: AlwaysStoppedAnimation<Color>(NordicColors.primary),
              ),
            ),
            if (message != null) ...[
              const SizedBox(height: 16.0),
              Text(
                message!,
                textAlign: TextAlign.center,
                style: NordicTypography.bodyRegular.copyWith(
                  color: NordicColors.textSecondary,
                ),
              ),
            ],
          ],
        ),
      ),
    );
  }
}

/// Error state indicator with error message and retry action.
class ErrorStateWidget extends StatelessWidget {
  final String message;
  final String? title;
  final VoidCallback? onRetry;
  final String retryText;

  const ErrorStateWidget({
    super.key,
    required this.message,
    this.title,
    this.onRetry,
    this.retryText = 'Thử lại',
  });

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24.0),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Container(
              padding: const EdgeInsets.all(16.0),
              decoration: const BoxDecoration(
                color: NordicColors.errorContainer,
                shape: BoxShape.circle,
              ),
              child: const Icon(
                Icons.error_outline_rounded,
                size: 40.0,
                color: NordicColors.error,
              ),
            ),
            const SizedBox(height: 16.0),
            Text(
              title ?? 'Đã có lỗi xảy ra',
              textAlign: TextAlign.center,
              style: NordicTypography.h3.copyWith(
                color: NordicColors.textTitle,
              ),
            ),
            const SizedBox(height: 8.0),
            Text(
              message,
              textAlign: TextAlign.center,
              style: NordicTypography.bodyRegular.copyWith(
                color: NordicColors.textSecondary,
              ),
            ),
            if (onRetry != null) ...[
              const SizedBox(height: 20.0),
              ElevatedButton.icon(
                onPressed: onRetry,
                icon: const Icon(Icons.refresh_rounded, size: 18.0),
                label: Text(retryText),
              ),
            ],
          ],
        ),
      ),
    );
  }
}

/// Empty state indicator when no records or items are available.
class EmptyStateWidget extends StatelessWidget {
  final String title;
  final String? subtitle;
  final IconData icon;
  final Widget? action;

  const EmptyStateWidget({
    super.key,
    required this.title,
    this.subtitle,
    this.icon = Icons.inbox_outlined,
    this.action,
  });

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24.0),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Container(
              padding: const EdgeInsets.all(20.0),
              decoration: const BoxDecoration(
                color: NordicColors.surfaceSubtle,
                shape: BoxShape.circle,
              ),
              child: Icon(
                icon,
                size: 44.0,
                color: NordicColors.secondaryAccent,
              ),
            ),
            const SizedBox(height: 16.0),
            Text(
              title,
              textAlign: TextAlign.center,
              style: NordicTypography.h3.copyWith(
                color: NordicColors.textTitle,
              ),
            ),
            if (subtitle != null) ...[
              const SizedBox(height: 8.0),
              Text(
                subtitle!,
                textAlign: TextAlign.center,
                style: NordicTypography.bodyRegular.copyWith(
                  color: NordicColors.textSecondary,
                ),
              ),
            ],
            if (action != null) ...[
              const SizedBox(height: 20.0),
              action!,
            ],
          ],
        ),
      ),
    );
  }
}
