import 'package:flutter/material.dart';

import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../models/vn_admin_units.dart';

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

/// Matches raw city name against keys of [vietnamAdminUnits].
/// Strips prefix 'Thành phố', 'Tỉnh', 'TP.', etc., and ignores case/accents.
String? matchAdminCity(String? rawCity) {
  if (rawCity == null || rawCity.trim().isEmpty) return null;
  final trimmed = rawCity.trim();
  if (vietnamAdminUnits.containsKey(trimmed)) return trimmed;

  String stripPrefix(String s) =>
      foldVietnamese(s)
          .replaceAll('thanh pho', '')
          .replaceAll('tp.', '')
          .replaceAll('tp', '')
          .replaceAll('tinh', '')
          .replaceAll(RegExp(r'[^a-z0-9]'), '')
          .trim();

  final foldedRaw = stripPrefix(trimmed);
  if (foldedRaw.isEmpty) return null;

  for (final key in vietnamAdminUnits.keys) {
    if (stripPrefix(key) == foldedRaw) {
      return key;
    }
  }

  for (final key in vietnamAdminUnits.keys) {
    final stripped = stripPrefix(key);
    if (stripped.isNotEmpty &&
        (stripped.contains(foldedRaw) || foldedRaw.contains(stripped))) {
      return key;
    }
  }

  return null;
}

/// Matches raw ward/district name (and optional addressLine/formattedAddress) against wards in [city].
/// Strips prefix 'Phường', 'Xã', 'Thị trấn', 'P.', etc., and ignores case/accents.
String? matchAdminWard(
  String? city,
  String? rawWard, {
  String? addressLine,
  String? formattedAddress,
}) {
  if (city == null) return null;
  final wards = vietnamAdminUnits[city];
  if (wards == null || wards.isEmpty) return null;

  String stripPrefix(String s) =>
      foldVietnamese(s)
          .replaceAll('phuong', '')
          .replaceAll('p.', '')
          .replaceAll('xa', '')
          .replaceAll('thi tran', '')
          .replaceAll('tt.', '')
          .replaceAll(RegExp(r'[^a-z0-9]'), '')
          .trim();

  String normalize(String s) =>
      foldVietnamese(s).replaceAll(RegExp(r'[^a-z0-9]'), '');

  if (rawWard != null && rawWard.trim().isNotEmpty) {
    final trimmed = rawWard.trim();
    if (wards.contains(trimmed)) return trimmed;

    final strippedRaw = stripPrefix(trimmed);
    if (strippedRaw.isNotEmpty) {
      for (final w in wards) {
        if (stripPrefix(w) == strippedRaw) {
          return w;
        }
      }
      for (final w in wards) {
        final sw = stripPrefix(w);
        if (sw.isNotEmpty &&
            (sw.contains(strippedRaw) || strippedRaw.contains(sw))) {
          return w;
        }
      }
    }
  }

  final extraSources = [
    if (addressLine != null && addressLine.isNotEmpty) addressLine,
    if (formattedAddress != null && formattedAddress.isNotEmpty)
      formattedAddress,
  ];

  for (final source in extraSources) {
    final normSource = normalize(source);
    for (final w in wards) {
      final strippedW = stripPrefix(w);
      if (strippedW.length >= 3 && normSource.contains(strippedW)) {
        return w;
      }
    }
  }

  return null;
}

/// Required read-only field of the address form that opens a searchable list of [options] (province, then ward).
/// A [value] outside [options] (an address saved before the catalogue) is shown as is and fails validation with
/// [notInListMessage], so the customer chooses again. Field and sheet shapes come from the theme (MOB-UI-14).
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
      // Shape from the theme: [NordicRadius.sheet] on top.
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
        labelText: label,
        helperText: helperText,
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
                    hintText: 'Tìm (gõ không dấu cũng được)',
                    prefixIcon: Icon(Icons.search_rounded),
                  ),
                  onChanged: (text) => setState(() => _query = text),
                ),
              ),
              const SizedBox(height: 8.0),
              Expanded(
                child: matches.isEmpty
                    ? Center(
                        child: Text(
                          'Không tìm thấy. Thử gõ tên khác.',
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
