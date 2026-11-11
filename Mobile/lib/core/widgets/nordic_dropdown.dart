import 'package:flutter/material.dart';

import '../theme/nordic_colors.dart';
import '../theme/nordic_radius.dart';
import '../theme/nordic_typography.dart';

/// One choice of a [NordicDropdown]: what the app keeps (`value`) and what the person reads (`label`).
class NordicDropdownOption<T> {
  final T value;
  final String label;

  const NordicDropdownOption(this.value, this.label);
}

/// A single choice among many options, shown as one field that opens the list under it (Nordic Care design system).
/// Use it from four options up; two or three options stay as pills. The field looks like [NordicTextInput].
class NordicDropdown<T> extends StatelessWidget {
  final T value;
  final List<NordicDropdownOption<T>> options;
  final ValueChanged<T> onChanged;
  final String? label;

  /// Read by screen readers; defaults to the label.
  final String? semanticLabel;

  /// Greys the field out and ignores taps (for example while a request is in flight).
  final bool enabled;

  const NordicDropdown({
    super.key,
    required this.value,
    required this.options,
    required this.onChanged,
    this.label,
    this.semanticLabel,
    this.enabled = true,
  }) : assert(options.length > 0);

  @override
  Widget build(BuildContext context) {
    final field = DropdownButtonFormField<T>(
      // A new value from the parent rebuilds the field, which keeps its own selection otherwise.
      key: ValueKey<T>(value),
      initialValue: value,
      isExpanded: true,
      borderRadius: NordicRadius.fieldAll,
      icon: const Icon(Icons.keyboard_arrow_down_rounded),
      iconEnabledColor: NordicColors.textTitle,
      dropdownColor: NordicColors.surface,
      style: NordicTypography.bodyRegular.copyWith(
        color: NordicColors.textTitle,
      ),
      decoration: InputDecoration(
        filled: true,
        fillColor: enabled ? NordicColors.surface : NordicColors.surfaceSubtle,
        contentPadding: const EdgeInsets.symmetric(
          horizontal: 16.0,
          vertical: 14.0,
        ),
        border: _border(NordicColors.border, 1.0),
        enabledBorder: _border(NordicColors.border, 1.0),
        disabledBorder: _border(NordicColors.border, 1.0),
        focusedBorder: _border(NordicColors.primary, 1.5),
      ),
      items: [
        for (final o in options)
          DropdownMenuItem<T>(
            value: o.value,
            child: Text(o.label, overflow: TextOverflow.ellipsis),
          ),
      ],
      onChanged: enabled
          ? (picked) {
              if (picked != null && picked != value) onChanged(picked);
            }
          : null,
    );

    final text = label;
    return Semantics(
      container: true,
      label: semanticLabel ?? text,
      child: text == null
          ? field
          : Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  text,
                  style: NordicTypography.labelMedium.copyWith(
                    fontWeight: FontWeight.w600,
                    color: NordicColors.textTitle,
                  ),
                ),
                const SizedBox(height: 6.0),
                field,
              ],
            ),
    );
  }

  static OutlineInputBorder _border(Color color, double width) =>
      OutlineInputBorder(
        borderRadius: NordicRadius.fieldAll,
        borderSide: BorderSide(color: color, width: width),
      );
}
