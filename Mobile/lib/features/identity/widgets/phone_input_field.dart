import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';

/// Clean phone input field with Vietnamese prefix badge.
class PhoneInputField extends StatelessWidget {
  final TextEditingController controller;
  final ValueChanged<String>? onChanged;
  final String? errorText;
  final bool enabled;

  const PhoneInputField({
    super.key,
    required this.controller,
    this.onChanged,
    this.errorText,
    this.enabled = true,
  });

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 12.0, vertical: 14.0),
              decoration: BoxDecoration(
                color: NordicColors.surfaceSubtle,
                borderRadius: BorderRadius.circular(12.0),
                border: Border.all(color: NordicColors.border),
              ),
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  const Text('🇻🇳', style: TextStyle(fontSize: 16.0)),
                  const SizedBox(width: 6.0),
                  Text(
                    '+84',
                    style: NordicTypography.labelMedium.copyWith(
                      color: NordicColors.textTitle,
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(width: 8.0),
            Expanded(
              child: TextField(
                controller: controller,
                enabled: enabled,
                keyboardType: TextInputType.phone,
                inputFormatters: [
                  FilteringTextInputFormatter.digitsOnly,
                  LengthLimitingTextInputFormatter(11),
                ],
                onChanged: onChanged,
                style: NordicTypography.bodyLarge.copyWith(
                  fontWeight: FontWeight.w600,
                  letterSpacing: 1.0,
                ),
                decoration: InputDecoration(
                  hintText: '0912 345 678',
                  hintStyle: NordicTypography.bodyLarge.copyWith(
                    color: NordicColors.textSecondary.withValues(alpha: 0.6),
                    fontWeight: FontWeight.w400,
                  ),
                  contentPadding: const EdgeInsets.symmetric(horizontal: 16.0, vertical: 14.0),
                  suffixIcon: controller.text.isNotEmpty
                      ? IconButton(
                          icon: const Icon(Icons.clear_rounded, size: 18.0),
                          onPressed: () {
                            controller.clear();
                            onChanged?.call('');
                          },
                        )
                      : null,
                ),
              ),
            ),
          ],
        ),
        if (errorText != null) ...[
          const SizedBox(height: 6.0),
          Text(
            errorText!,
            style: NordicTypography.bodySmall.copyWith(
              color: NordicColors.error,
              fontWeight: FontWeight.w500,
            ),
          ),
        ],
      ],
    );
  }
}
