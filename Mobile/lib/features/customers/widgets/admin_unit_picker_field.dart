import 'package:flutter/material.dart';

import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';

const _foldedLetters = {
  'a': 'àáạảãâầấậẩẫăằắặẳẵ',
  'e': 'èéẹẻẽêềếệểễ',
  'i': 'ìíịỉĩ',
  'o': 'òóọỏõôồốộổỗơờớợởỡ',
  'u': 'ùúụủũưừứựửữ',
  'y': 'ỳýỵỷỹ',
  'd': 'đ',
};

final Map<String, String> _foldMap = {
  for (final entry in _foldedLetters.entries)
    for (final rune in entry.value.runes) String.fromCharCode(rune): entry.key,
};

/// Lower case without Vietnamese diacritics, so "sai gon" finds "Phường Sài Gòn".
String foldVietnamese(String text) {
  final buffer = StringBuffer();
  for (final rune in text.toLowerCase().runes) {
    final char = String.fromCharCode(rune);
    buffer.write(_foldMap[char] ?? char);
  }
  return buffer.toString();
}

/// Whether [name] matches the search [query] (case and diacritics ignored; an empty query matches everything).
bool matchesAdminUnitQuery(String name, String query) {
  final folded = foldVietnamese(query.trim());
  return folded.isEmpty || foldVietnamese(name).contains(folded);
}

/// Required read-only field of the address form that opens a searchable list of [options] (province, then ward).
/// A [value] outside [options] (an address saved before the catalogue) is shown as is and fails validation with
/// [notInListMessage], so the customer chooses again.
class AdminUnitPickerField extends StatelessWidget {
  final String label;
  final String? value;
  final List<String> options;
  final bool enabled;
  final String? helperText;
  final String requiredMessage;
  final String notInListMessage;
  final ValueChanged<String> onSelected;

  const AdminUnitPickerField({
    super.key,
    required this.label,
    required this.value,
    required this.options,
    required this.requiredMessage,
    required this.notInListMessage,
    required this.onSelected,
    this.enabled = true,
    this.helperText,
  });

  Future<void> _openPicker(BuildContext context) async {
    final picked = await showModalBottomSheet<String>(
      context: context,
      isScrollControlled: true,
      backgroundColor: NordicColors.surface,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(16.0)),
      ),
      builder: (context) => _AdminUnitSheet(title: label, options: options),
    );
    if (picked != null && picked != value) {
      onSelected(picked);
    }
  }

  @override
  Widget build(BuildContext context) {
    // Keyed by the value: a new choice rebuilds the field with it (setting a controller during build would mark the Form dirty).
    return TextFormField(
      key: ValueKey('$label|$value'),
      initialValue: value ?? '',
      readOnly: true,
      enabled: enabled,
      onTap: () => _openPicker(context),
      decoration: InputDecoration(
        labelText: '$label*',
        helperText: helperText,
        border: const OutlineInputBorder(),
        suffixIcon: const Icon(Icons.arrow_drop_down_rounded),
      ),
      validator: (_) {
        final current = value;
        if (current == null || current.trim().isEmpty) return requiredMessage;
        if (!options.contains(current)) return notInListMessage;
        return null;
      },
    );
  }
}

class _AdminUnitSheet extends StatefulWidget {
  final String title;
  final List<String> options;

  const _AdminUnitSheet({required this.title, required this.options});

  @override
  State<_AdminUnitSheet> createState() => _AdminUnitSheetState();
}

class _AdminUnitSheetState extends State<_AdminUnitSheet> {
  String _query = '';

  @override
  Widget build(BuildContext context) {
    final matches = widget.options
        .where((name) => matchesAdminUnitQuery(name, _query))
        .toList();

    return SafeArea(
      child: Padding(
        padding: EdgeInsets.only(
          bottom: MediaQuery.of(context).viewInsets.bottom,
        ),
        child: SizedBox(
          height: MediaQuery.of(context).size.height * 0.75,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Padding(
                padding: const EdgeInsets.fromLTRB(16.0, 16.0, 16.0, 8.0),
                child: Text('Chọn ${widget.title}', style: NordicTypography.h3),
              ),
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 16.0),
                child: TextField(
                  autofocus: true,
                  decoration: const InputDecoration(
                    hintText: 'Tìm kiếm (có thể gõ không dấu)',
                    prefixIcon: Icon(Icons.search_rounded),
                    border: OutlineInputBorder(),
                  ),
                  onChanged: (text) => setState(() => _query = text),
                ),
              ),
              const SizedBox(height: 8.0),
              Expanded(
                child: matches.isEmpty
                    ? Center(
                        child: Text(
                          'Không tìm thấy kết quả phù hợp',
                          style: NordicTypography.bodySmall,
                        ),
                      )
                    : ListView.builder(
                        itemCount: matches.length,
                        itemBuilder: (context, index) => ListTile(
                          title: Text(
                            matches[index],
                            style: NordicTypography.bodyRegular.copyWith(
                              color: NordicColors.textTitle,
                            ),
                          ),
                          onTap: () => Navigator.pop(context, matches[index]),
                        ),
                      ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
