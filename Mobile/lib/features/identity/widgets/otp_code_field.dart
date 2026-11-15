import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../../core/theme/nordic_colors.dart';

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
            // Never `enabled: false`: a disabled field drops the focus and the keyboard, so after
            // a wrong code backspace went nowhere (#901). While [enabled] is false the typing is
            // ignored instead, and the focus stays.
            autofocus: true,
            keyboardType: TextInputType.number,
            inputFormatters: [
              TextInputFormatter.withFunction(
                (oldValue, newValue) => widget.enabled ? newValue : oldValue,
              ),
              FilteringTextInputFormatter.digitsOnly,
              LengthLimitingTextInputFormatter(6),
            ],
          ),
        ),

        // 6 styled digits cells
        GestureDetector(
          onTap: () {
            if (!widget.enabled) return;
            if (_focusNode.hasFocus) {
              // Already focused but the keyboard was closed (e.g. the back key): open it again.
              SystemChannels.textInput.invokeMethod<void>('TextInput.show');
            } else {
              _focusNode.requestFocus();
            }
          },
          // ui-templates `shared/otp.html`: 6 equal cells, the next one outlined in primary.
          child: Row(
            children: List.generate(6, (index) {
              final isFilled = index < code.length;
              final isCurrent = index == code.length;
              final digit = isFilled ? code[index] : '';

              return Expanded(
                child: Container(
                  height: 60.0,
                  margin: EdgeInsets.only(left: index == 0 ? 0 : 8.0),
                  decoration: BoxDecoration(
                    color: NordicColors.surface,
                    borderRadius: BorderRadius.circular(12.0),
                    border: Border.all(
                      color: isCurrent
                          ? NordicColors.primary
                          : NordicColors.border,
                      width: isCurrent ? 2.0 : 1.0,
                    ),
                  ),
                  alignment: Alignment.center,
                  child: Text(
                    digit,
                    style: const TextStyle(
                      fontSize: 24.0,
                      fontWeight: FontWeight.w700,
                      color: NordicColors.textTitle,
                      fontFeatures: [FontFeature.tabularFigures()],
                    ),
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
