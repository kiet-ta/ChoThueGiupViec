import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../../core/motion/nordic_motion.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';

/// Clean phone input field with Vietnamese prefix badge.
class PhoneInputField extends StatelessWidget {
  final TextEditingController controller;
  final ValueChanged<String>? onChanged;
  final String? errorText;
  final bool enabled;

  /// The number is a valid mobile number: a check mark says so while typing (MOB-UI-12).
  final bool isValid;

  const PhoneInputField({
    super.key,
    required this.controller,
    this.onChanged,
    this.errorText,
    this.enabled = true,
    this.isValid = false,
  });

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            // ui-templates `shared/phone.html`: a fixed +84 box beside the number.
            Container(
              width: 92.0,
              height: 52.0,
              alignment: Alignment.center,
              decoration: BoxDecoration(
                color: NordicColors.surface,
                borderRadius: BorderRadius.circular(12.0),
                border: Border.all(color: NordicColors.border),
              ),
              child: const Text(
                '+84',
                style: TextStyle(
                  fontSize: 15.0,
                  fontWeight: FontWeight.w600,
                  color: NordicColors.textTitle,
                ),
              ),
            ),
            const SizedBox(width: 10.0),
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
                style: const TextStyle(
                  fontSize: 20.0,
                  fontWeight: FontWeight.w600,
                  letterSpacing: 0.8,
                  color: NordicColors.textTitle,
                  fontFeatures: [FontFeature.tabularFigures()],
                ),
                decoration: InputDecoration(
                  hintText: '0912 345 678',
                  hintStyle: TextStyle(
                    fontSize: 20.0,
                    fontWeight: FontWeight.w400,
                    color: NordicColors.textSecondary.withValues(alpha: 0.6),
                  ),
                  contentPadding: const EdgeInsets.symmetric(
                    horizontal: 16.0,
                    vertical: 14.0,
                  ),
                  suffixIcon: controller.text.isNotEmpty
                      ? Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            _ValidMark(visible: isValid),
                            IconButton(
                              icon: const Icon(Icons.close, size: 18.0),
                              onPressed: () {
                                controller.clear();
                                onChanged?.call('');
                              },
                            ),
                          ],
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

/// The check mark of a valid number: grows in on the standard spring, or just appears under
/// reduced motion.
class _ValidMark extends StatelessWidget {
  final bool visible;

  const _ValidMark({required this.visible});

  @override
  Widget build(BuildContext context) {
    final reduced = NordicMotion.reduced(context);
    return AnimatedScale(
      key: const Key('phone-valid-mark'),
      scale: visible ? 1.0 : 0.0,
      duration: reduced ? Duration.zero : NordicMotion.standard.settleDuration,
      curve: SpringCurve(NordicMotion.standard),
      child: const Icon(
        Icons.check_circle,
        size: 20.0,
        color: NordicColors.primary,
        semanticLabel: 'Số hợp lệ',
      ),
    );
  }
}
