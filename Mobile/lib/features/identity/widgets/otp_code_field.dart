import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';

/// 6-digit OTP code entry widget with individual display cells.
class OtpCodeField extends StatefulWidget {
  final TextEditingController controller;
  final ValueChanged<String>? onCompleted;
  final ValueChanged<String>? onChanged;
  final bool enabled;

  const OtpCodeField({
    super.key,
    required this.controller,
    this.onCompleted,
    this.onChanged,
    this.enabled = true,
  });

  @override
  State<OtpCodeField> createState() => _OtpCodeFieldState();
}

class _OtpCodeFieldState extends State<OtpCodeField> {
  final FocusNode _focusNode = FocusNode();

  @override
  void initState() {
    super.initState();
    widget.controller.addListener(_handleTextChange);
  }

  @override
  void dispose() {
    widget.controller.removeListener(_handleTextChange);
    _focusNode.dispose();
    super.dispose();
  }

  void _handleTextChange() {
    final text = widget.controller.text;
    widget.onChanged?.call(text);
    if (text.length == 6) {
      widget.onCompleted?.call(text);
    }
    if (mounted) setState(() {});
  }

  @override
  Widget build(BuildContext context) {
    final code = widget.controller.text;

    return Stack(
      children: [
        // Invisible input field catching keyboard input
        Opacity(
          opacity: 0.0,
          child: TextField(
            controller: widget.controller,
            focusNode: _focusNode,
            enabled: widget.enabled,
            autofocus: true,
            keyboardType: TextInputType.number,
            inputFormatters: [
              FilteringTextInputFormatter.digitsOnly,
              LengthLimitingTextInputFormatter(6),
            ],
          ),
        ),

        // 6 styled digits cells
        GestureDetector(
          onTap: () {
            if (widget.enabled) {
              _focusNode.requestFocus();
            }
          },
          child: Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: List.generate(6, (index) {
              final isFilled = index < code.length;
              final isCurrent = index == code.length;
              final digit = isFilled ? code[index] : '';

              return Container(
                width: 48.0,
                height: 56.0,
                decoration: BoxDecoration(
                  color: NordicColors.surface,
                  borderRadius: BorderRadius.circular(12.0),
                  border: Border.all(
                    color: isCurrent
                        ? NordicColors.primary
                        : (isFilled ? NordicColors.secondaryAccent : NordicColors.border),
                    width: isCurrent ? 2.0 : 1.0,
                  ),
                  boxShadow: isCurrent
                      ? [
                          BoxShadow(
                            color: NordicColors.primary.withValues(alpha: 0.12),
                            blurRadius: 8.0,
                            offset: const Offset(0, 2),
                          ),
                        ]
                      : null,
                ),
                alignment: Alignment.center,
                child: Text(
                  digit,
                  style: NordicTypography.h2.copyWith(
                    fontWeight: FontWeight.w700,
                    color: NordicColors.textTitle,
                  ),
                ),
              );
            }),
          ),
        ),
      ],
    );
  }
}
